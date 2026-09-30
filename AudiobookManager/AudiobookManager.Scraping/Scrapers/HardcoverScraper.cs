using System.Globalization;
using System.Text;
using System.Text.Json;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Extensions;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Scraping.Scrapers;

public class HardcoverScraper : IScraper
{
    private const string _hardcoverDomain = "hardcover.app";
    private const string _hardcoverBaseUrl = $"https://{_hardcoverDomain}";
    private const string _hardcoverApiUrl = "https://api.hardcover.app/v1/graphql";
    private const string _sourceName = "Hardcover";
    private const int _maxNumGenresToGet = 5;

    private static readonly IList<string> _ignoredGenres = new List<string> { "Fiction" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;
    private readonly ILogger<HardcoverScraper> _logger;
    private readonly AudiobookManagerSettings _settings;

    public HardcoverScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper,
        ILogger<HardcoverScraper> logger, IOptions<AudiobookManagerSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
        _logger = logger;
        _settings = settings.Value;
    }

    public string SourceName => _sourceName;

    public bool RequiresApiKey => true;

    public bool IsApiKeyConfigured => !string.IsNullOrEmpty(_settings.HardcoverApiKey);

    public bool IsSource(string sourceName) => string.Equals(sourceName, _sourceName, StringComparison.InvariantCultureIgnoreCase);

    public bool SupportsUrl(string url) => ScraperUrl.HasHost(url, _hardcoverDomain);

    public async Task<IList<MetadataSearchResult>> Search(string searchTerm)
    {
        var query = """
            query SearchBooks($query: String!) {
              search(query: $query, query_type: "books", per_page: 15, page: 1) {
                results
              }
            }
            """;

        var variables = new { query = searchTerm };
        var responseElement = await ExecuteGraphqlQuery(query, variables);

        var resultsJson = responseElement.GetNestedProperty("data", "search", "results");

        JsonElement hitsArray;
        if (resultsJson.ValueKind == JsonValueKind.Array)
        {
            hitsArray = resultsJson;
        }
        else if (resultsJson.ValueKind == JsonValueKind.Object &&
                 resultsJson.TryGetProperty("hits", out var hitsElement) &&
                 hitsElement.ValueKind == JsonValueKind.Array)
        {
            hitsArray = hitsElement;
        }
        else
        {
            return new List<MetadataSearchResult>();
        }

        var results = new List<MetadataSearchResult>();
        foreach (var hit in hitsArray.EnumerateArray())
        {
            try
            {
                var result = ParseSearchHit(hit);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Hardcover search result");
            }
        }

        // One mapping call for the whole result set, not per hit: the mapper is scoped and
        // shared by the concurrent scrapers in SearchMultiple, and its own comments document
        // the DbContext single-operation constraint that a per-hit fan-out would stress
        // (AudibleScraper.Search made exactly that mistake with one await per hit). The
        // per-book overload returns group-for-group, so each book's series stays its own
        // instead of being re-sliced out of a flattened result on trust.
        try
        {
            var perBookSeries = results
                .Select(r => (IList<MetadataSeriesSearchResult>)(r.Series ?? []))
                .ToList();
            var mappedGroups = await _bookSeriesMapper.MapBookSeriesPerBook(perBookSeries);

            for (var i = 0; i < results.Count; i++)
            {
                results[i].Series = mappedGroups[i].ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to map series names for Hardcover search results");
        }

        return results;
    }

    public async Task<MetadataSearchResult> GetBookDetails(string bookUrl)
    {
        var slug = ParseBookSlugFromUrl(bookUrl);
        var bookElement = await GetBookBySlug(slug);

        if (bookElement.ValueKind == JsonValueKind.Null || bookElement.ValueKind == JsonValueKind.Undefined)
        {
            throw new Exception($"Book not found on Hardcover: {bookUrl}");
        }

        return await ParseBookDetails(bookElement, bookUrl);
    }

    public bool SupportsSeriesLookup => true;

    // series_by_pk and book_series were verified against Hardcover's published GraphQL
    // schema - so a failure in GetSeriesBooks is transient (network/HTTP/timeout, already
    // retried and rate limited by the "hardcover" client's handlers) and is allowed to
    // propagate like every other scrape failure in this file.
    // Schema reference (docs.hardcover.app is often egress-blocked in sandboxes): the SDL is
    // mirrored unauthenticated at https://raw.githubusercontent.com/hardcoverapp/hardcover-docs/main/schema.graphql
    //
    // Series search (SearchSeries below) intentionally does NOT use a `series(where: ...)`
    // query with `_ilike`/`_like` - Hardcover's API rejects those operators server-side
    // ("ilike and related operations are not permitted on this server", HTTP 403) even
    // though they're still present in the published schema types. This is documented under
    // "Limitations" at https://docs.hardcover.app/api/getting-started/#limitations (disabled:
    // _like, _nlike, _ilike, _niregex, _nregex, _iregex, _regex, _nsimilar, _similar; also a
    // 30s query timeout / 2s search() timeout, and no browser-side use of the API key).
    // Fuzzy/typo-tolerant name search is only available through the same Typesense-backed
    // `search()` query used by Search() above, with query_type "Series" - see
    // https://github.com/hardcoverapp/hardcover-docs/blob/main/src/content/docs/api/guides/Searching.mdx

    // Both book_series(...) selections below exclude alternate-language/translated editions.
    // The `canonical_id: {_is_null: true}` filter catches only the editions Hardcover explicitly
    // records as duplicates: a translated edition whose `canonical_id` points back at the
    // canonical (original-language) book row is dropped. That alone is not enough, though -
    // translated editions are frequently their own independent `book` rows with `canonical_id`
    // null, linked into the series at the same position as the original without pointing at
    // anything, so they passed the filter and produced a roster with several entries per
    // position (e.g. Jack Reacher Part 2 showing the English, French and Thai titles). The
    // per-position dedupe in BuildSeriesResult/RetainMostPopularPerPosition closes that gap: it
    // keeps only the most popular book per position, the recipe Hardcover documents in "Getting
    // All Books in a Series" at
    // https://github.com/hardcoverapp/hardcover-docs/blob/main/src/content/docs/api/guides/GettingBooksInSeries.mdx
    // - filter canonical_id, filter is_partial_book, and keep the most popular book per position
    // ordered by users_count desc. That is the same thing the hardcover.app series page shows.
    //
    // The dedupe is done in C# rather than SQL `distinct_on: position` on purpose: DISTINCT ON
    // would collapse every null/unnumbered-position entry into a single row, and it would let a
    // compilation evict the individual book at the same position - breaking the invariant that
    // the full roster including compilations is always stored here, which the per-series
    // "Include omnibus editions" setting (a SeriesService choice) depends on. So the C# dedupe
    // keeps compilations and non-compilations as separate partitions, and never groups null
    // positions. Omnibus/box-set entries (e.g. a "Books 1-4" bundle, flagged by `compilation`
    // on either the book_series link row or the book itself - contributors sometimes only tag
    // one of the two) are deliberately NOT filtered out: whether to keep them is a per-series
    // choice, so `compilation` is selected and left for the caller (SeriesService).
    //
    // `is_partial_book: {_eq: false}` mirrors the website, which hides partial editions: per
    // Hardcover's guide, a partial edition is a book that only contains part of the contents of
    // another book. `_eq` and `_is_null` are plain equality filters, not any of the disabled
    // pattern-matching operators (see the "Limitations" note above), so both where clauses are
    // safe against the API's filter restrictions.
    private const string _seriesBooksQuery = """
        query GetSeriesBooks($id: Int!) {
          series_by_pk(id: $id) {
            id
            name
            slug
            book_series(
              order_by: [{position: asc}, {book: {users_count: desc}}]
              where: {book: {canonical_id: {_is_null: true}, is_partial_book: {_eq: false}}}
            ) {
              position
              compilation
              book {
                id
                title
                slug
                release_date
                compilation
                users_count
                cached_image
                contributions {
                  contribution
                  author {
                    id
                    name
                  }
                }
              }
            }
          }
        }
        """;

    private const string _seriesBooksBySlugQuery = """
        query GetSeriesBooksBySlug($slug: String!) {
          series(where: {slug: {_eq: $slug}}, limit: 1) {
            id
            name
            slug
            book_series(
              order_by: [{position: asc}, {book: {users_count: desc}}]
              where: {book: {canonical_id: {_is_null: true}, is_partial_book: {_eq: false}}}
            ) {
              position
              compilation
              book {
                id
                title
                slug
                release_date
                compilation
                users_count
                cached_image
                contributions {
                  contribution
                  author {
                    id
                    name
                  }
                }
              }
            }
          }
        }
        """;


    public async Task<IList<SeriesSearchResult>> SearchSeries(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new List<SeriesSearchResult>();
        }

        var query = """
            query SearchSeries($query: String!) {
              search(query: $query, query_type: "Series", per_page: 10, page: 1) {
                results
              }
            }
            """;

        var variables = new { query = searchTerm.Trim() };
        var responseElement = await ExecuteGraphqlQuery(query, variables);

        var resultsJson = responseElement.GetNestedProperty("data", "search", "results");

        JsonElement hitsArray;
        if (resultsJson.ValueKind == JsonValueKind.Array)
        {
            hitsArray = resultsJson;
        }
        else if (resultsJson.ValueKind == JsonValueKind.Object &&
                 resultsJson.TryGetProperty("hits", out var hitsElement) &&
                 hitsElement.ValueKind == JsonValueKind.Array)
        {
            hitsArray = hitsElement;
        }
        else
        {
            return new List<SeriesSearchResult>();
        }

        var results = new List<SeriesSearchResult>();
        foreach (var hit in hitsArray.EnumerateArray())
        {
            try
            {
                var parsed = ParseSeriesSearchHit(hit);
                if (parsed is not null)
                {
                    results.Add(parsed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Hardcover series search result");
            }
        }

        return results;
    }

    public async Task<SeriesSearchResult?> GetSeriesBooks(string seriesIdOrUrl)
    {
        var seriesId = ParseSeriesIdentifier(seriesIdOrUrl);
        if (seriesId is not null)
        {
            var variables = new { id = seriesId.Value };
            var responseElement = await ExecuteGraphqlQuery(_seriesBooksQuery, variables);
            return BuildSeriesResult(responseElement.GetNestedProperty("data", "series_by_pk"), seriesIdOrUrl);
        }

        // A manually-pasted series URL is usually slug-only (e.g. hardcover.app/series/harry-potter)
        // rather than the numeric id ParseSeriesIdentifier looks for, so fall back to a slug lookup.
        var slug = ParseSeriesSlug(seriesIdOrUrl);
        if (slug is not null)
        {
            var variables = new { slug };
            var responseElement = await ExecuteGraphqlQuery(_seriesBooksBySlugQuery, variables);
            var seriesArray = responseElement.GetNestedProperty("data", "series");
            var seriesElement = seriesArray.ValueKind == JsonValueKind.Array && seriesArray.GetArrayLength() > 0
                ? seriesArray[0]
                : default;
            return BuildSeriesResult(seriesElement, seriesIdOrUrl);
        }

        _logger.LogWarning("Could not extract a Hardcover series id or slug from {SeriesIdOrUrl}", seriesIdOrUrl);
        return null;
    }

    private SeriesSearchResult? BuildSeriesResult(JsonElement seriesElement, string seriesIdOrUrl)
    {
        if (seriesElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var result = ParseSeriesElement(seriesElement);
        if (result is null)
        {
            return null;
        }

        var roster = new List<SeriesRosterCandidate>();
        if (seriesElement.TryGetProperty("book_series", out var bookSeriesElement) &&
            bookSeriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in bookSeriesElement.EnumerateArray())
            {
                try
                {
                    var candidate = ParseSeriesRosterEntry(entry);
                    if (candidate is not null)
                    {
                        roster.Add(candidate);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse Hardcover series roster entry for series {SeriesIdOrUrl}", seriesIdOrUrl);
                }
            }
        }

        // The roster can carry a translated edition per language at the same position (see the
        // note above the series query constants), so keep only the most popular book per part -
        // the same thing the hardcover.app series page shows.
        foreach (var winner in RetainMostPopularPerPosition(roster))
        {
            result.Books.Add(winner.Book);
        }

        result.BookCount ??= result.Books.Count;
        return result;
    }

    private SeriesSearchResult? ParseSeriesSearchHit(JsonElement hit)
    {
        // Same "document" unwrapping as ParseSearchHit() - Typesense search results may
        // nest the document under a "document" property or be the document itself.
        var document = hit.TryGetProperty("document", out var docElement) &&
                       docElement.ValueKind == JsonValueKind.Object
            ? docElement
            : hit;

        var id = document.GetPropertyValueOrNull("id");
        var name = document.GetPropertyValueOrNull("name");

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        // A series with no slug has no working Hardcover URL - same disproven "id works as a URL
        // segment" premise the book path relied on (confirmed live: even a real book's own
        // numeric id 404s as a path segment). Leave SourceUrl null rather than emit a link that
        // can never resolve.
        var slug = document.GetPropertyValueOrNull("slug");

        var result = new SeriesSearchResult(id, name)
        {
            SourceUrl = slug is null ? null : $"{_hardcoverBaseUrl}/series/{slug}",
        };

        if (document.TryGetProperty("books_count", out var booksCountElement) &&
            booksCountElement.ValueKind == JsonValueKind.Number)
        {
            result.BookCount = booksCountElement.GetInt32();
        }

        var authorName = document.GetPropertyValueOrNull("author_name");
        if (!string.IsNullOrEmpty(authorName))
        {
            result.Authors.Add(authorName);
        }

        return result;
    }

    private static SeriesSearchResult? ParseSeriesElement(JsonElement seriesElement)
    {
        if (seriesElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetScalarOrNull(seriesElement, "id");
        var name = seriesElement.GetPropertyValueOrNull("name");

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        var slug = seriesElement.GetPropertyValueOrNull("slug");

        var result = new SeriesSearchResult(id, name)
        {
            SourceUrl = slug is null ? null : $"{_hardcoverBaseUrl}/series/{slug}",
        };

        if (seriesElement.TryGetProperty("books_count", out var booksCountElement) &&
            booksCountElement.ValueKind == JsonValueKind.Number)
        {
            result.BookCount = booksCountElement.GetInt32();
        }

        if (seriesElement.TryGetProperty("author", out var authorElement) &&
            authorElement.ValueKind == JsonValueKind.Object)
        {
            var authorName = authorElement.GetPropertyValueOrNull("name");
            if (!string.IsNullOrEmpty(authorName))
            {
                result.Authors.Add(authorName);
            }
        }

        return result;
    }

    /// <summary>
    /// A parsed series roster entry: the public <see cref="SeriesExpectedBookResult"/> plus the
    /// book-level fields the per-position dedupe in
    /// <see cref="RetainMostPopularPerPosition"/> needs to pick a winner - the book's popularity
    /// (<c>users_count</c>), its numeric id (the tiebreak), and the normalized numeric position it
    /// groups under. This is a private scrape-internal type; it is never exposed on any public model.
    /// </summary>
    private record SeriesRosterCandidate(
        SeriesExpectedBookResult Book,
        long? UsersCount,
        long? BookId,
        double? NumericPosition)
    {
    }

    /// <summary>
    /// Keeps only the most popular book per position - translated editions are usually recorded as
    /// their own independent book rows at the same position (see the note above the series query
    /// constants), and the user only wants one entry per part. See <see cref="RosterGroupKey"/> for
    /// the exact grouping semantics and <see cref="IsStrictlyBetter"/> for how a winner is chosen.
    /// </summary>
    private static IList<SeriesRosterCandidate> RetainMostPopularPerPosition(IList<SeriesRosterCandidate> roster)
    {
        if (roster.Count <= 1)
        {
            return roster;
        }

        // Winner index per group key. Winners are chosen in wire order, replacing the current
        // winner only when strictly better, so emitting in wire order below preserves the original
        // relative order (no re-sort).
        var winners = new Dictionary<string, int>();
        for (var i = 0; i < roster.Count; i++)
        {
            var key = RosterGroupKey(roster[i], i);
            if (!winners.ContainsKey(key) || IsStrictlyBetter(roster[i], roster[winners[key]]))
            {
                winners[key] = i;
            }
        }

        var kept = new List<SeriesRosterCandidate>();
        for (var i = 0; i < roster.Count; i++)
        {
            var key = RosterGroupKey(roster[i], i);
            if (winners.ContainsKey(key) && winners[key] == i)
            {
                kept.Add(roster[i]);
            }
        }

        return kept;
    }

    /// <summary>
    /// The dedupe group an entry falls into. Compilations and non-compilations are separate
    /// partitions at the same position, so the per-series "Include omnibus editions" setting keeps
    /// working - the full roster including one omnibus per position must still be stored. An entry
    /// with no usable numeric position gets a unique group so it is never collapsed into a shared
    /// row, deliberately unlike SQL distinct_on: position, which would collapse every
    /// null-position entry into a single one. Numeric positions are formatted with the invariant
    /// round-trip representation so the key does not depend on the process locale.
    /// </summary>
    private static string RosterGroupKey(SeriesRosterCandidate candidate, int index)
    {
        if (candidate.NumericPosition is null)
        {
            return $"__unnumbered__{index}";
        }

        return $"{candidate.NumericPosition.Value.ToString("R", CultureInfo.InvariantCulture)}|{candidate.Book.IsCompilation}";
    }

    /// <summary>
    /// Is <paramref name="candidate"/> a strictly better pick than <paramref name="current"/> for
    /// the same group? Higher users_count wins (a missing users_count ranks below any known one);
    /// a tie is broken by the lower numeric book id (a missing id ranks last); a tie on both falls
    /// back to the first-encountered entry.
    /// </summary>
    private static bool IsStrictlyBetter(SeriesRosterCandidate candidate, SeriesRosterCandidate current)
    {
        if (candidate.UsersCount is null && current.UsersCount is not null)
        {
            return false;
        }
        if (candidate.UsersCount is not null && current.UsersCount is null)
        {
            return true;
        }
        if (candidate.UsersCount is not null && current.UsersCount is not null)
        {
            var candidateUsers = (long)candidate.UsersCount;
            var currentUsers = (long)current.UsersCount;
            if (candidateUsers != currentUsers)
            {
                return candidateUsers > currentUsers;
            }
        }

        if (candidate.BookId is null && current.BookId is not null)
        {
            return false;
        }
        if (candidate.BookId is not null && current.BookId is null)
        {
            return true;
        }
        if (candidate.BookId is not null && current.BookId is not null)
        {
            var candidateId = (long)candidate.BookId;
            var currentId = (long)current.BookId;
            return candidateId < currentId;
        }

        // Tie on users_count and id (including both missing) - keep the first-encountered.
        return false;
    }

    private static SeriesRosterCandidate? ParseSeriesRosterEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("book", out var bookElement) ||
            bookElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = bookElement.GetPropertyValueOrNull("title");
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var bookId = GetScalarOrNull(bookElement, "id");
        var slug = bookElement.GetPropertyValueOrNull("slug");

        // Writing credits only - the same Narrator role filter the author bibliography and
        // upcoming-releases paths apply, so a series refresh attributes each book to the same
        // authors an author-side refresh would.
        var authors = new List<string>();
        if (bookElement.TryGetProperty("contributions", out var contributionsElement) &&
            contributionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var contribution in contributionsElement.EnumerateArray())
            {
                var role = contribution.GetPropertyValueOrNull("contribution");
                if (string.Equals(role, "Narrator", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                if (!contribution.TryGetProperty("author", out var authorElement) ||
                    authorElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var authorName = authorElement.GetPropertyValueOrNull("name");
                if (!string.IsNullOrEmpty(authorName))
                {
                    authors.Add(authorName);
                }
            }
        }

        int? year = null;
        DateOnly? parsedReleaseDate = null;
        var releaseDate = bookElement.GetPropertyValueOrNull("release_date");
        if (releaseDate is not null &&
            DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            year = parsedDate.Year;
            parsedReleaseDate = DateOnly.FromDateTime(parsedDate);
        }

        string? position = null;
        if (entry.TryGetProperty("position", out var positionElement))
        {
            if (positionElement.ValueKind == JsonValueKind.Number)
            {
                var posValue = positionElement.GetSingle();
                position = posValue == Math.Floor(posValue)
                    ? ((int)posValue).ToString(CultureInfo.InvariantCulture)
                    : posValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (positionElement.ValueKind == JsonValueKind.String)
            {
                position = positionElement.GetString();
            }
        }

        // Contributors sometimes only tag one of the two records, so either flag being set is
        // enough to treat the entry as a compilation.
        var linkIsCompilation = entry.TryGetProperty("compilation", out var linkCompilationElement) &&
            linkCompilationElement.ValueKind == JsonValueKind.True;
        var bookIsCompilation = bookElement.TryGetProperty("compilation", out var bookCompilationElement) &&
            bookCompilationElement.ValueKind == JsonValueKind.True;

        var book = new SeriesExpectedBookResult(title)
        {
            SourceBookId = bookId,
            Position = position,
            Year = year,
            ReleaseDate = parsedReleaseDate,
            SourceUrl = slug is null ? null : $"{_hardcoverBaseUrl}/books/{slug}",
            ImageUrl = ParseCachedImage(bookElement),
            IsCompilation = linkIsCompilation || bookIsCompilation,
            Authors = authors,
        };

        // Users_count is the book's popularity on Hardcover - the per-position dedupe keeps the
        // most popular book. `Int!` in the schema, but Hasura may still deliver it as a string.
        long? usersCount = null;
        var usersCountRaw = GetScalarOrNull(bookElement, "users_count");
        if (usersCountRaw is not null &&
            long.TryParse(usersCountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedUsersCount))
        {
            usersCount = parsedUsersCount;
        }

        // Numeric book id, the tiebreak when two entries are equally popular.
        long? numericBookId = null;
        if (bookId is not null &&
            long.TryParse(bookId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBookId))
        {
            numericBookId = parsedBookId;
        }

        // The numeric position this entry groups under for the dedupe. A null, non-numeric or
        // non-finite position (some contributors use labels) means the entry is never grouped
        // with another.
        double? numericPosition = null;
        if (position is not null &&
            double.TryParse(position, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedPosition) &&
            double.IsFinite(parsedPosition))
        {
            numericPosition = parsedPosition;
        }

        return new SeriesRosterCandidate(book, usersCount, numericBookId, numericPosition);
    }

    /// <summary>
    /// Reads a scalar property that the API may return either as a JSON string or a JSON
    /// number. The shared GetPropertyValueOrNull helper calls GetString(), which throws on
    /// numbers - and Hasura returns series/book ids as numbers.
    /// </summary>
    private static string? GetScalarOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// Accepts a bare numeric series id, or a Hardcover series URL whose path ends in one.
    /// Slug-only URLs return null here - callers fall back to <see cref="ParseSeriesSlug"/>.
    /// </summary>
    private static int? ParseSeriesIdentifier(string seriesIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(seriesIdOrUrl))
        {
            return null;
        }

        if (int.TryParse(seriesIdOrUrl, out var directId))
        {
            return directId;
        }

        if (!Uri.TryCreate(seriesIdOrUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var lastSegment = segments.LastOrDefault();

        if (lastSegment is not null && int.TryParse(lastSegment, out var pathId))
        {
            return pathId;
        }

        return null;
    }

    /// <summary>
    /// Extracts a non-numeric slug from the last path segment of a Hardcover series URL (e.g.
    /// https://hardcover.app/series/harry-potter -&gt; "harry-potter"). Only meaningful for an
    /// absolute URL - a bare string reaching here already failed <see cref="ParseSeriesIdentifier"/>
    /// and isn't necessarily a slug, so non-URL input returns null rather than being guessed at.
    /// </summary>
    private static string? ParseSeriesSlug(string seriesIdOrUrl)
    {
        if (!Uri.TryCreate(seriesIdOrUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.LastOrDefault();
    }

    public bool SupportsAuthorLookup => true;

    public async Task<IList<AuthorSearchResult>> SearchAuthors(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new List<AuthorSearchResult>();
        }

        // query_type is "Author" (singular) per docs.hardcover.app/api/guides/searching - "Authors"
        // (plural) is not a recognized query_type and silently returns zero results rather than
        // erroring, which is why author search returned nothing for every query.
        var query = """
            query SearchAuthors($query: String!) {
              search(query: $query, query_type: "Author", per_page: 10, page: 1) {
                results
              }
            }
            """;

        var variables = new { query = searchTerm.Trim() };
        var responseElement = await ExecuteGraphqlQuery(query, variables);

        var resultsJson = responseElement.GetNestedProperty("data", "search", "results");

        JsonElement hitsArray;
        if (resultsJson.ValueKind == JsonValueKind.Array)
        {
            hitsArray = resultsJson;
        }
        else if (resultsJson.ValueKind == JsonValueKind.Object &&
                 resultsJson.TryGetProperty("hits", out var hitsElement) &&
                 hitsElement.ValueKind == JsonValueKind.Array)
        {
            hitsArray = hitsElement;
        }
        else
        {
            return new List<AuthorSearchResult>();
        }

        var results = new List<AuthorSearchResult>();
        foreach (var hit in hitsArray.EnumerateArray())
        {
            try
            {
                var parsed = ParseAuthorSearchHit(hit);
                if (parsed is not null)
                {
                    results.Add(parsed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Hardcover author search result");
            }
        }

        return results;
    }

    private AuthorSearchResult? ParseAuthorSearchHit(JsonElement hit)
    {
        var document = hit.TryGetProperty("document", out var docElement) &&
                       docElement.ValueKind == JsonValueKind.Object
            ? docElement
            : hit;

        var id = document.GetPropertyValueOrNull("id");
        var name = document.GetPropertyValueOrNull("name");

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        // An author with no slug has no working Hardcover URL - same disproven "id works as a URL
        // segment" premise the book path relied on (confirmed live: even a real book's own
        // numeric id 404s as a path segment). Leave SourceUrl null rather than emit a link that
        // can never resolve.
        var slug = document.GetPropertyValueOrNull("slug");

        var result = new AuthorSearchResult(id, name)
        {
            SourceUrl = slug is null ? null : $"{_hardcoverBaseUrl}/authors/{slug}",
        };

        if (document.TryGetProperty("books_count", out var booksCountElement) &&
            booksCountElement.ValueKind == JsonValueKind.Number)
        {
            result.BookCount = booksCountElement.GetInt32();
        }

        return result;
    }

    // Mirrors the recipe the series roster query above documents: canonical_id/is_partial_book
    // filter out translated/partial duplicates. release_date >= $today is filtered server-side
    // (not just required to be non-null) so a prolific author's entire dated back catalog is
    // never downloaded and thrown away client-side - $today is the caller's DateOnly.FromDateTime
    // (UtcNow) formatted as an ISO date, the same value ParseUpcomingBook's own defense-in-depth
    // lower-bound check compares against. `_gte` is a plain comparison operator, not one of the
    // disabled pattern-matching operators (see the "Limitations" note above the series query).
    // `limit: 100` bounds the response regardless: unlikely for a single author, but nothing
    // caps how many books Hardcover records against one, and this is a periodic background poll,
    // not a page a user is actively waiting on. Unlike the series roster query, no per-position
    // popularity dedupe is needed here - the caller (UpcomingReleasesService) dedupes discovered
    // releases by source book id across every followed author/series.
    private const string _authorUpcomingBooksQuery = """
        query GetAuthorUpcomingBooks($id: Int!, $today: date!) {
          authors_by_pk(id: $id) {
            id
            name
            contributions(
              where: {book: {canonical_id: {_is_null: true}, is_partial_book: {_eq: false}, release_date: {_gte: $today}}}
              order_by: [{book: {release_date: desc}}]
              limit: 100
            ) {
              contribution
              book {
                id
                title
                slug
                release_date
                cached_image
                book_series {
                  position
                  series {
                    id
                    name
                  }
                }
              }
            }
          }
        }
        """;

    public async Task<IList<UpcomingReleaseResult>> GetAuthorUpcomingReleases(string authorSourceId)
    {
        if (!int.TryParse(authorSourceId, out var id))
        {
            _logger.LogWarning("Could not parse a numeric Hardcover author id from {AuthorSourceId}", authorSourceId);
            return new List<UpcomingReleaseResult>();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var responseElement = await ExecuteGraphqlQuery(_authorUpcomingBooksQuery, new { id, today });
        var authorElement = responseElement.GetNestedProperty("data", "authors_by_pk");
        if (authorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new List<UpcomingReleaseResult>();
        }

        var results = new List<UpcomingReleaseResult>();
        if (!authorElement.TryGetProperty("contributions", out var contributionsElement) ||
            contributionsElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var contribution in contributionsElement.EnumerateArray())
        {
            try
            {
                // Only the author's own writing credits - a book this person merely narrates
                // is not "their" upcoming release.
                var role = contribution.GetPropertyValueOrNull("contribution");
                if (string.Equals(role, "Narrator", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                if (!contribution.TryGetProperty("book", out var bookElement) ||
                    bookElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var result = ParseUpcomingBook(bookElement, today);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse a Hardcover upcoming release for author {AuthorSourceId}", authorSourceId);
            }
        }

        return results;
    }

    // Same canonical_id/is_partial_book filter as the upcoming-releases query above, but no
    // release_date lower bound - this backs the author's full bibliography roster, which needs
    // the whole bibliography (missing AND upcoming), not just what's still ahead.
    // `limit: 300` is a defensive cap: a single author's own SearchAuthors "books_count" is
    // shown to the user before matching, so a hard limit far past any real bibliography just
    // guards against the same pathological-source case every other bounded scrape query does.
    private const string _authorAllBooksQuery = """
        query GetAuthorAllBooks($id: Int!) {
          authors_by_pk(id: $id) {
            id
            name
            contributions(
              where: {book: {canonical_id: {_is_null: true}, is_partial_book: {_eq: false}}}
              order_by: [{book: {release_date: desc}}]
              limit: 300
            ) {
              contribution
              book {
                id
                title
                slug
                release_date
                cached_image
                book_series {
                  position
                  series {
                    id
                    name
                  }
                }
              }
            }
          }
        }
        """;

    public async Task<IList<AuthorBookResult>> GetAuthorBooks(string authorSourceId)
    {
        if (!int.TryParse(authorSourceId, out var id))
        {
            // An unparseable id is a caller problem, not an empty bibliography: the caller
            // refreshes its roster on this result, and an empty fetch would prune the whole
            // stored roster. Make the failure explicit so the caller aborts instead.
            _logger.LogWarning("Could not parse a numeric Hardcover author id from {AuthorSourceId}", authorSourceId);
            throw new AuthorNotFoundException(
                $"Could not parse \"{authorSourceId}\" as a numeric Hardcover author id.");
        }

        var responseElement = await ExecuteGraphqlQuery(_authorAllBooksQuery, new { id });

        // A missing "authors_by_pk" key (a malformed/empty envelope) folds into the same "no
        // such author" failure as an explicit null - never a raw KeyNotFoundException that the
        // roster refresh would treat as an unspecified error.
        JsonElement authorElement;
        try
        {
            authorElement = responseElement.GetNestedProperty("data", "authors_by_pk");
        }
        catch (KeyNotFoundException)
        {
            throw new AuthorNotFoundException(
                $"Hardcover returned no author for source id \"{authorSourceId}\" - the author may have been deleted or merged on the source side, or the source responded with an empty result.");
        }
        if (authorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            // The source resolves no such author (deleted/merged upstream, or a transient empty
            // response) - same explicit-failure rule as the unparseable id above.
            throw new AuthorNotFoundException(
                $"Hardcover returned no author for source id \"{authorSourceId}\" - the author may have been deleted or merged on the source side, or the source responded with an empty result.");
        }

        var results = new List<AuthorBookResult>();
        if (!authorElement.TryGetProperty("contributions", out var contributionsElement) ||
            contributionsElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        // The query's `limit: 300` truncates silently server-side - there is no separate overflow
        // signal to check, so hitting the cap exactly is the only local evidence a prolific
        // author's bibliography was cut off. Unlike the reconciliation caps (which refuse rather
        // than silently truncate), the caller stores whatever comes back and stamps
        // LastRefreshedAt regardless, so this is the only place the truncation becomes observable
        // at all - log it rather than let it pass unnoticed.
        if (contributionsElement.GetArrayLength() >= 300)
        {
            _logger.LogWarning(
                "Hardcover author {AuthorSourceId} (id {AuthorId}) returned {Count} bibliography contributions, hitting the 300-row query cap - the fetched roster is likely truncated and missing some of this author's books",
                authorSourceId, id, contributionsElement.GetArrayLength());
        }

        foreach (var contribution in contributionsElement.EnumerateArray())
        {
            try
            {
                var role = contribution.GetPropertyValueOrNull("contribution");
                if (string.Equals(role, "Narrator", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                if (!contribution.TryGetProperty("book", out var bookElement) ||
                    bookElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var result = ParseAuthorBook(bookElement);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse a Hardcover bibliography entry for author {AuthorSourceId}", authorSourceId);
            }
        }

        return results;
    }

    private AuthorBookResult? ParseAuthorBook(JsonElement bookElement)
    {
        var title = bookElement.GetPropertyValueOrNull("title");
        var bookId = GetScalarOrNull(bookElement, "id");
        if (string.IsNullOrEmpty(title) || bookId is null)
        {
            return null;
        }

        int? year = null;
        DateOnly? releaseDate = null;
        var releaseDateRaw = bookElement.GetPropertyValueOrNull("release_date");
        if (releaseDateRaw is not null &&
            DateTime.TryParse(releaseDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            year = parsedDate.Year;
            releaseDate = DateOnly.FromDateTime(parsedDate);
        }

        var slug = bookElement.GetPropertyValueOrNull("slug");

        string? seriesSourceId = null;
        string? seriesName = null;
        string? seriesPosition = null;
        // A book can front multiple series, and the author feed has no single "position" of its
        // own, so the first entry (the source's featured one) stands in for the book's series
        // placement - the same first-entry handling as ParseUpcomingBook.
        if (bookElement.TryGetProperty("book_series", out var bookSeriesElement) &&
            bookSeriesElement.ValueKind == JsonValueKind.Array &&
            bookSeriesElement.GetArrayLength() > 0)
        {
            var first = bookSeriesElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Object)
            {
                seriesName = seriesElement.GetPropertyValueOrNull("name");
                seriesSourceId = GetScalarOrNull(seriesElement, "id");
                if (first.TryGetProperty("position", out var positionElement))
                {
                    seriesPosition = FormatSeriesPosition(positionElement);
                }
            }
        }

        return new AuthorBookResult(bookId, title)
        {
            Year = year,
            ReleaseDate = releaseDate,
            SourceUrl = slug is null ? null : $"{_hardcoverBaseUrl}/books/{slug}",
            ImageUrl = ParseCachedImage(bookElement),
            SeriesSourceId = seriesSourceId,
            SeriesName = seriesName,
            SeriesPosition = seriesPosition,
        };
    }

    // Same release_date >= $today server-side filter and defensive limit as the author query
    // above - a long-running series (or one whose omnibus/box-set editions are excluded
    // elsewhere but still counted here) should not force a full-roster download on every poll.
    private const string _seriesUpcomingBooksQuery = """
        query GetSeriesUpcomingBooks($id: Int!, $today: date!) {
          series_by_pk(id: $id) {
            id
            name
            book_series(
              order_by: [{position: asc}]
              where: {book: {canonical_id: {_is_null: true}, is_partial_book: {_eq: false}, release_date: {_gte: $today}}}
              limit: 100
            ) {
              position
              book {
                id
                title
                slug
                release_date
                cached_image
              }
            }
          }
        }
        """;

    public async Task<IList<UpcomingReleaseResult>> GetSeriesUpcomingReleases(string seriesSourceId)
    {
        if (!int.TryParse(seriesSourceId, out var id))
        {
            _logger.LogWarning("Could not parse a numeric Hardcover series id from {SeriesSourceId}", seriesSourceId);
            return new List<UpcomingReleaseResult>();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var responseElement = await ExecuteGraphqlQuery(_seriesUpcomingBooksQuery, new { id, today });
        var seriesElement = responseElement.GetNestedProperty("data", "series_by_pk");
        if (seriesElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new List<UpcomingReleaseResult>();
        }

        var seriesName = seriesElement.GetPropertyValueOrNull("name");

        var results = new List<UpcomingReleaseResult>();
        if (!seriesElement.TryGetProperty("book_series", out var bookSeriesElement) ||
            bookSeriesElement.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var entry in bookSeriesElement.EnumerateArray())
        {
            try
            {
                if (!entry.TryGetProperty("book", out var bookElement) ||
                    bookElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var result = ParseUpcomingBook(bookElement, today);
                if (result is null)
                {
                    continue;
                }

                result.SeriesName = seriesName;
                result.SeriesSourceId = seriesSourceId;
                if (entry.TryGetProperty("position", out var positionElement))
                {
                    result.SeriesPosition = FormatSeriesPosition(positionElement);
                }

                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse a Hardcover upcoming release for series {SeriesSourceId}", seriesSourceId);
            }
        }

        return results;
    }

    /// <summary>
    /// Shared book-row parsing for both upcoming-release queries: a valid, still-future
    /// release_date and a title are required, and the first series the book carries (if any)
    /// is attached - the author query's <c>book_series</c> only asks for id/name, not position,
    /// since a book can front multiple series and the author feed has no single "position" of
    /// its own; the series-scoped query attaches its own known position separately.
    /// </summary>
    private UpcomingReleaseResult? ParseUpcomingBook(JsonElement bookElement, DateOnly today)
    {
        var title = bookElement.GetPropertyValueOrNull("title");
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var releaseDateRaw = bookElement.GetPropertyValueOrNull("release_date");
        if (releaseDateRaw is null ||
            !DateOnly.TryParse(releaseDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
        {
            return null;
        }

        // Only genuinely upcoming books are ingested - the back catalog is not what "upcoming
        // releases" means, and UpcomingReleaseRepository keeps whatever was already stored
        // regardless of date, so a book that later slips into the past stays without being
        // re-fetched here.
        if (releaseDate < today)
        {
            return null;
        }

        var bookId = GetScalarOrNull(bookElement, "id");
        var slug = bookElement.GetPropertyValueOrNull("slug");

        if (bookId is null)
        {
            return null;
        }

        var result = new UpcomingReleaseResult(bookId, title, releaseDate)
        {
            SourceUrl = slug is null ? null : $"{_hardcoverBaseUrl}/books/{slug}",
            ImageUrl = ParseCachedImage(bookElement),
        };

        if (bookElement.TryGetProperty("book_series", out var bookSeriesElement) &&
            bookSeriesElement.ValueKind == JsonValueKind.Array)
        {
            var first = bookSeriesElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Object)
            {
                result.SeriesName = seriesElement.GetPropertyValueOrNull("name");
                result.SeriesSourceId = GetScalarOrNull(seriesElement, "id");
                if (first.TryGetProperty("position", out var positionElement))
                {
                    result.SeriesPosition = FormatSeriesPosition(positionElement);
                }
            }
        }

        return result;
    }

    private async Task<JsonElement> GetBookBySlug(string slug)
    {
        var responseElement = await ExecuteGraphqlQuery(_bookDetailsQuery, new { slug });
        var booksArray = responseElement.GetNestedProperty("data", "books");

        if (booksArray.ValueKind == JsonValueKind.Array && booksArray.GetArrayLength() > 0)
        {
            return booksArray[0];
        }

        return default;
    }

    private const string _bookDetailsQuery = """
        query GetBook($slug: String!) {
          books(where: {slug: {_eq: $slug}}, limit: 1) {
            id
            title
            subtitle
            description
            slug
            release_date
            rating
            ratings_count
            cached_image
            cached_tags
            contributions {
              contribution
              author {
                name
              }
            }
            book_series {
              position
              series {
                name
              }
            }
            default_audio_edition {
              subtitle
              cached_image
              contributions {
                contribution
                author {
                  name
                }
              }
              isbn_13
              asin
              audio_seconds
              publisher {
                name
              }
              language {
                language
              }
            }
            default_physical_edition {
              subtitle
              isbn_13
              asin
              publisher {
                name
              }
              language {
                language
              }
            }
          }
        }
        """;

    private MetadataSearchResult? ParseSearchHit(JsonElement hit)
    {
        // Each hit may contain a nested "document" property (Typesense format)
        // or be the document itself (direct array format)
        var document = hit.TryGetProperty("document", out var docElement) &&
                       docElement.ValueKind == JsonValueKind.Object
            ? docElement
            : hit;

        var idStr = document.GetPropertyValueOrNull("id");
        if (idStr is null)
        {
            return null;
        }

        var title = document.GetPropertyValueOrNull("title");
        if (title is null)
        {
            return null;
        }

        // A book with no slug has no working Hardcover URL at all - confirmed live: even a real
        // book's own numeric database id 404s when used as the path segment, since Hardcover
        // books are addressed only by slug. Skip the hit entirely rather than emit a link that
        // can never resolve, matching the id/title null checks above.
        var slug = document.GetPropertyValueOrNull("slug");
        if (slug is null)
        {
            // Live verification found zero Hardcover books without a slug, so this should never
            // fire - but if that premise is ever wrong, the drop should be visible rather than
            // silently shrinking the result set.
            _logger.LogDebug("Dropping Hardcover search hit {BookId} ({Title}) with no slug - no working URL to give it", idStr, title);
            return null;
        }

        var url = $"{_hardcoverBaseUrl}/books/{slug}";

        var subtitle = document.GetPropertyValueOrNull("subtitle");

        var authors = new List<Person>();
        if (document.TryGetProperty("author_names", out var authorNamesElement) &&
            authorNamesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var authorName in authorNamesElement.EnumerateArray())
            {
                var name = authorName.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    authors.Add(new Person(name));
                }
            }
        }

        string? imageUrl = null;
        if (document.TryGetProperty("image", out var imageElement))
        {
            if (imageElement.ValueKind == JsonValueKind.Object)
            {
                imageUrl = imageElement.GetPropertyValueOrNull("url");
            }
            else if (imageElement.ValueKind == JsonValueKind.String)
            {
                imageUrl = imageElement.GetString();
            }
        }

        int? year = null;
        var releaseDate = document.GetPropertyValueOrNull("release_date");
        if (releaseDate is not null && DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            year = parsedDate.Year;
        }
        else
        {
            var releaseYear = document.GetPropertyValueOrNull("release_year");
            if (releaseYear is not null && int.TryParse(releaseYear, out var parsedYear))
            {
                year = parsedYear;
            }
        }

        int? numberOfRatings = null;
        if (document.TryGetProperty("ratings_count", out var ratingsCountElement) &&
            ratingsCountElement.ValueKind == JsonValueKind.Number)
        {
            numberOfRatings = ratingsCountElement.GetInt32();
        }

        float? rating = null;
        if (document.TryGetProperty("rating", out var ratingElement) &&
            ratingElement.ValueKind == JsonValueKind.Number)
        {
            rating = ratingElement.GetSingle();
        }

        return new MetadataSearchResult(url, title)
        {
            Authors = authors,
            Narrators = new List<Person>(),
            Subtitle = subtitle,
            Year = year,
            ImageUrl = imageUrl,
            Rating = rating,
            NumberOfRatings = numberOfRatings,
            Series = ParseSearchHitSeries(document),
            Genres = new List<string>(),
        };
    }

    /// <summary>
    /// Series info straight from the Typesense search document, so the results preview can
    /// show it without the extra GetBookDetails query the Audible scraper avoids too. The
    /// search document carries the featured series (a book_series-shaped object with
    /// position + nested series.name) and a flat series_names list without positions.
    /// Confirmed live: absent is null or {} - never a throw - and position is a float.
    /// </summary>
    private static IList<MetadataSeriesSearchResult> ParseSearchHitSeries(JsonElement document)
    {
        var series = new List<MetadataSeriesSearchResult>();

        // The book_series-shaped featured object; also tolerate a string-encoded JSON
        // variant, mirroring how cached_image/cached_tags sometimes arrive in this API.
        if (document.TryGetProperty("featured_series", out var featuredElement))
        {
            JsonElement? featuredObj = featuredElement.ValueKind switch
            {
                JsonValueKind.Object => featuredElement,
                JsonValueKind.String when TryDeserializeJson(featuredElement.GetString(), out var parsed) => parsed,
                _ => null,
            };

            if (featuredObj is not null &&
                featuredObj.Value.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Object)
            {
                var name = seriesElement.GetPropertyValueOrNull("name");
                if (!string.IsNullOrEmpty(name))
                {
                    series.Add(new MetadataSeriesSearchResult(name)
                    {
                        SeriesPart = FormatSeriesPosition(
                            featuredObj.Value.TryGetProperty("position", out var positionElement)
                                ? positionElement
                                : default),
                    });
                }
            }
        }

        if (document.TryGetProperty("series_names", out var seriesNamesElement) &&
            seriesNamesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var nameElement in seriesNamesElement.EnumerateArray())
            {
                var name = nameElement.GetString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // The featured entry already carries this series (with its position).
                if (series.Any(s => string.Equals(s.SeriesName, name, StringComparison.InvariantCultureIgnoreCase)))
                {
                    continue;
                }

                series.Add(new MetadataSeriesSearchResult(name));
            }
        }

        return series;
    }

    private static bool TryDeserializeJson(string? json, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(json);
            return element.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Formats a book_series position the same way <see cref="ParseSeries"/> does for the
    /// details path - 5.0 becomes "5", 5.5 stays "5.5" - so search preview and details
    /// never disagree about the part value for the same book.
    /// </summary>
    private static string? FormatSeriesPosition(JsonElement positionElement)
    {
        if (positionElement.ValueKind == JsonValueKind.Number)
        {
            var posValue = positionElement.GetSingle();
            return posValue == Math.Floor(posValue)
                ? ((int)posValue).ToString(CultureInfo.InvariantCulture)
                : posValue.ToString(CultureInfo.InvariantCulture);
        }

        if (positionElement.ValueKind == JsonValueKind.String)
        {
            return positionElement.GetString();
        }

        return null;
    }

    private async Task<MetadataSearchResult> ParseBookDetails(JsonElement bookElement, string bookUrl)
    {
        string? bookName = null;
        string? subtitle = null;
        try
        {
            // The title is kept whole rather than guessed apart on a colon - nothing in
            // Hardcover's docs or schema documents "title embeds subtitle" as a convention, and a
            // title's own colon is not reliably a separator (e.g. the time in
            // "4:50 from Paddington"). Recovering a subtitle from the title is an explicit,
            // opt-in choice at apply time (see TitleSplitter), not a guess baked in here.
            var fullTitle = bookElement.GetPropertyValueOrNull("title");
            if (fullTitle is not null)
            {
                bookName = fullTitle.Trim();
            }

            subtitle = ResolveSubtitle(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse title for {BookUrl}", bookUrl);
        }

        IList<Person> authors = new List<Person>();
        IList<Person> narrators = new List<Person>();
        try
        {
            (authors, narrators) = ParseContributions(bookElement);

            // Hardcover records narrators on the audio edition; the book-level contributions
            // are mostly authors only (confirmed live for "A Wizard of Earthsea" and "The
            // Hobbit"). The edition's narrators win, the book-level ones are the fallback.
            var audioEditionForNarrators = GetEditionElement(bookElement, "default_audio_edition");
            if (audioEditionForNarrators is not null)
            {
                var editionNarrators = ParseContributions(audioEditionForNarrators.Value).Narrators;
                if (editionNarrators.Count > 0)
                {
                    narrators = editionNarrators;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse contributions for {BookUrl}", bookUrl);
        }

        string? imageUrl = null;
        try
        {
            // The audio edition's cover is the audiobook's own (square) art; the book-level
            // cached_image is usually the print cover. Fall back to the book-level one when the
            // audio edition has none.
            var audioEditionForCover = GetEditionElement(bookElement, "default_audio_edition");
            imageUrl = (audioEditionForCover is null ? null : ParseCachedImage(audioEditionForCover.Value))
                       ?? ParseCachedImage(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse image for {BookUrl}", bookUrl);
        }

        int? year = null;
        try
        {
            var releaseDate = bookElement.GetPropertyValueOrNull("release_date");
            if (releaseDate is not null && DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                year = parsedDate.Year;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse year for {BookUrl}", bookUrl);
        }

        string? description = null;
        try
        {
            description = SanitizeHtml(bookElement.GetPropertyValueOrNull("description"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse description for {BookUrl}", bookUrl);
        }

        IList<string> genres = new List<string>();
        try
        {
            genres = ParseGenres(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse genres for {BookUrl}", bookUrl);
        }

        float? rating = null;
        int? numberOfRatings = null;
        try
        {
            if (bookElement.TryGetProperty("rating", out var ratingElement) &&
                ratingElement.ValueKind == JsonValueKind.Number)
            {
                rating = ratingElement.GetSingle();
            }

            if (bookElement.TryGetProperty("ratings_count", out var ratingsCountElement) &&
                ratingsCountElement.ValueKind == JsonValueKind.Number)
            {
                numberOfRatings = ratingsCountElement.GetInt32();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse rating for {BookUrl}", bookUrl);
        }

        IList<MetadataSeriesSearchResult> series = new List<MetadataSeriesSearchResult>();
        try
        {
            series = await ParseSeries(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse series for {BookUrl}", bookUrl);
        }

        string? publisher = null;
        string? language = null;
        string? isbn = null;
        string? asin = null;
        try
        {
            var audioEdition = GetEditionElement(bookElement, "default_audio_edition");
            var physicalEdition = GetEditionElement(bookElement, "default_physical_edition");

            var edition = audioEdition ?? physicalEdition;

            if (edition is not null)
            {
                isbn = edition.Value.GetPropertyValueOrNull("isbn_13");
                asin = edition.Value.GetPropertyValueOrNull("asin");

                if (edition.Value.TryGetProperty("publisher", out var publisherElement) &&
                    publisherElement.ValueKind == JsonValueKind.Object)
                {
                    publisher = publisherElement.GetPropertyValueOrNull("name");
                }

                if (edition.Value.TryGetProperty("language", out var languageElement) &&
                    languageElement.ValueKind == JsonValueKind.Object)
                {
                    language = languageElement.GetPropertyValueOrNull("language");
                }
            }

            // Fall back to physical edition for ISBN/ASIN/publisher/language if audio edition didn't have them
            if (audioEdition is not null && physicalEdition is not null)
            {
                if (isbn is null)
                {
                    isbn = physicalEdition.Value.GetPropertyValueOrNull("isbn_13");
                }
                if (asin is null)
                {
                    asin = physicalEdition.Value.GetPropertyValueOrNull("asin");
                }
                if (publisher is null && physicalEdition.Value.TryGetProperty("publisher", out var pubElement) &&
                    pubElement.ValueKind == JsonValueKind.Object)
                {
                    publisher = pubElement.GetPropertyValueOrNull("name");
                }
                if (language is null && physicalEdition.Value.TryGetProperty("language", out var langElement) &&
                    langElement.ValueKind == JsonValueKind.Object)
                {
                    language = langElement.GetPropertyValueOrNull("language");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse edition details for {BookUrl}", bookUrl);
        }

        string? duration = null;
        try
        {
            var audioEdition = GetEditionElement(bookElement, "default_audio_edition");
            if (audioEdition is not null &&
                audioEdition.Value.TryGetProperty("audio_seconds", out var audioSecondsElement) &&
                audioSecondsElement.ValueKind == JsonValueKind.Number)
            {
                var totalSeconds = audioSecondsElement.GetInt32();
                var hours = totalSeconds / 3600;
                var minutes = (totalSeconds % 3600) / 60;
                duration = hours > 0 ? $"{hours} hrs and {minutes} mins" : $"{minutes} mins";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse duration for {BookUrl}", bookUrl);
        }

        return new MetadataSearchResult(bookUrl, bookName ?? string.Empty)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = duration,
            Year = year,
            Language = language,
            ImageUrl = imageUrl,
            Series = series,
            Description = description,
            Genres = genres,
            Rating = rating,
            NumberOfRatings = numberOfRatings,
            Copyright = null,
            Publisher = publisher,
            Asin = asin,
            Isbn = isbn,
        };
    }

    private static (IList<Person> Authors, IList<Person> Narrators) ParseContributions(JsonElement bookElement)
    {
        var authors = new List<Person>();
        var narrators = new List<Person>();

        if (!bookElement.TryGetProperty("contributions", out var contributionsElement) ||
            contributionsElement.ValueKind != JsonValueKind.Array)
        {
            return (authors, narrators);
        }

        foreach (var contribution in contributionsElement.EnumerateArray())
        {
            var role = contribution.GetPropertyValueOrNull("contribution");
            string? name = null;

            if (contribution.TryGetProperty("author", out var authorElement) &&
                authorElement.ValueKind == JsonValueKind.Object)
            {
                name = authorElement.GetPropertyValueOrNull("name");
            }

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var person = new Person(name) { Role = role };

            if (string.Equals(role, "Narrator", StringComparison.InvariantCultureIgnoreCase))
            {
                narrators.Add(person);
            }
            else
            {
                authors.Add(person);
            }
        }

        return (authors, narrators);
    }

    private static string? ParseCachedImage(JsonElement bookElement)
    {
        if (!bookElement.TryGetProperty("cached_image", out var cachedImageElement))
        {
            return null;
        }

        if (cachedImageElement.ValueKind == JsonValueKind.String)
        {
            var jsonStr = cachedImageElement.GetString();
            if (jsonStr is not null)
            {
                var imageObj = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                return imageObj.GetPropertyValueOrNull("url");
            }
        }
        else if (cachedImageElement.ValueKind == JsonValueKind.Object)
        {
            return cachedImageElement.GetPropertyValueOrNull("url");
        }

        return null;
    }

    private IList<string> ParseGenres(JsonElement bookElement)
    {
        var genres = new List<string>();

        if (!bookElement.TryGetProperty("cached_tags", out var cachedTagsElement))
        {
            return genres;
        }

        JsonElement tagsObj;
        if (cachedTagsElement.ValueKind == JsonValueKind.String)
        {
            var jsonStr = cachedTagsElement.GetString();
            if (jsonStr is null)
            {
                return genres;
            }
            tagsObj = JsonSerializer.Deserialize<JsonElement>(jsonStr);
        }
        else if (cachedTagsElement.ValueKind == JsonValueKind.Object)
        {
            tagsObj = cachedTagsElement;
        }
        else
        {
            return genres;
        }

        if (tagsObj.TryGetProperty("Genre", out var genreElement) &&
            genreElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genreElement.EnumerateArray())
            {
                var tag = genre.GetPropertyValueOrNull("tag");
                if (!string.IsNullOrEmpty(tag))
                {
                    genres.Add(tag);
                }
            }
        }

        return genres
            .Where(g => !_ignoredGenres.Any(ig => string.Equals(ig, g, StringComparison.InvariantCultureIgnoreCase)))
            .Take(_maxNumGenresToGet)
            .ToList();
    }

    private async Task<IList<MetadataSeriesSearchResult>> ParseSeries(JsonElement bookElement)
    {
        var series = new List<MetadataSeriesSearchResult>();

        if (!bookElement.TryGetProperty("book_series", out var bookSeriesElement) ||
            bookSeriesElement.ValueKind != JsonValueKind.Array)
        {
            return series;
        }

        foreach (var bs in bookSeriesElement.EnumerateArray())
        {
            string? seriesName = null;
            if (bs.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Object)
            {
                seriesName = seriesElement.GetPropertyValueOrNull("name");
            }

            if (string.IsNullOrEmpty(seriesName))
            {
                continue;
            }

            series.Add(new MetadataSeriesSearchResult(seriesName)
            {
                SeriesPart = FormatSeriesPosition(
                    bs.TryGetProperty("position", out var positionElement) ? positionElement : default)
            });
        }

        return await _bookSeriesMapper.MapBookSeries(series);
    }

    /// <summary>
    /// The book-level <c>subtitle</c> is community-edited and can be wrong (book 427401,
    /// "A Wizard of Earthsea", carries a business-book subtitle while both its editions have
    /// none). The default audio edition - falling back to the physical one - is the record this
    /// audiobook tool actually cares about, so its subtitle wins whenever the response carries
    /// the field at all, even as null. The book-level value is used only when neither edition
    /// reports a subtitle field.
    /// </summary>
    private static string? ResolveSubtitle(JsonElement bookElement)
    {
        foreach (var editionProperty in new[] { "default_audio_edition", "default_physical_edition" })
        {
            var edition = GetEditionElement(bookElement, editionProperty);
            if (edition is not null && edition.Value.TryGetProperty("subtitle", out _))
            {
                return edition.Value.GetPropertyValueOrNull("subtitle");
            }
        }

        return bookElement.GetPropertyValueOrNull("subtitle");
    }

    private static JsonElement? GetEditionElement(JsonElement bookElement, string editionProperty)
    {
        if (bookElement.TryGetProperty(editionProperty, out var editionElement) &&
            editionElement.ValueKind == JsonValueKind.Object)
        {
            return editionElement;
        }

        return null;
    }

    // Hardcover books are addressed only by slug in a URL - there is no numeric-id URL form at
    // all. Confirmed empirically: even the real "1984"'s own database id (379760) 404s at
    // https://hardcover.app/books/379760, and a live query for books with a null slug returns
    // zero rows, so a book with no slug does not exist in practice either. A previous version of
    // this method treated an all-digit last path segment as a database id instead of a slug,
    // which resolved https://hardcover.app/books/1984 (Orwell's book, whose slug is itself the
    // digits "1984") to whatever unrelated book happens to have database id 1984 - the last
    // segment is always a slug, numeric-looking or not.
    private static string ParseBookSlugFromUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new Exception($"Could not extract book identifier from Hardcover URL: {url}");
        }

        return segments.Last();
    }

    private async Task<JsonElement> ExecuteGraphqlQuery(string query, object variables)
    {
        var httpClient = _httpClientFactory.CreateClient("hardcover");

        var requestBody = JsonSerializer.Serialize(new
        {
            query,
            variables
        });

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(_hardcoverApiUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Hardcover API returned status {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var responseElement = JsonSerializer.Deserialize<JsonElement>(responseJson);

        if (responseElement.TryGetProperty("errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array &&
            errorsElement.GetArrayLength() > 0)
        {
            var firstError = errorsElement[0].GetPropertyValueOrNull("message") ?? "Unknown GraphQL error";
            throw new Exception($"Hardcover GraphQL error: {firstError}");
        }

        return responseElement;
    }

    private static string? SanitizeHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        var result = html
            .Replace("<br />", "\n")
            .Replace("<br>", "\n")
            .Replace("<br/>", "\n");

        // Remove HTML tags
        result = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", "");

        result = result
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"");

        return result.Trim();
    }
}
