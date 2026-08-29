using System.Globalization;
using System.Text;
using System.Text.Json;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Extensions;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    public bool SupportsUrl(string url) => url.Contains(_hardcoverDomain);

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

        return results;
    }

    public async Task<MetadataSearchResult> GetBookDetails(string bookUrl)
    {
        var bookIdentifier = ParseBookIdentifierFromUrl(bookUrl);

        var bookElement = bookIdentifier.Id is not null
            ? await GetBookById(bookIdentifier.Id.Value)
            : await GetBookBySlug(bookIdentifier.Slug!);

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

    // Both book_series(...) selections below exclude alternate-language/translated editions -
    // these are frequently recorded as their own `book` row (rather than just an `edition` of
    // the original) and linked into the series at the same position as the original;
    // canonical_id is non-null on these variants and points back at the canonical
    // (original-language) book, which is the row we keep.
    //
    // Omnibus/box-set entries (e.g. a "Books 1-4" bundle, flagged by `compilation` on either the
    // book_series link row or the book itself - contributors sometimes only tag one of the two)
    // are deliberately NOT filtered out here: whether to keep them is a per-series choice some
    // libraries genuinely own the omnibus rather than the individual books - so `compilation` is
    // selected and left for the caller (SeriesService) to filter based on that series' setting.
    private const string _seriesBooksQuery = """
        query GetSeriesBooks($id: Int!) {
          series_by_pk(id: $id) {
            id
            name
            slug
            book_series(
              order_by: {position: asc}
              where: {book: {canonical_id: {_is_null: true}}}
            ) {
              position
              compilation
              book {
                id
                title
                slug
                release_date
                compilation
              }
            }
          }
        }
        """;

    // `_eq` is a plain equality filter, not one of the disabled pattern-matching operators
    // (see the "Limitations" note above), so this is safe against the API's filter restrictions.
    private const string _seriesBooksBySlugQuery = """
        query GetSeriesBooksBySlug($slug: String!) {
          series(where: {slug: {_eq: $slug}}, limit: 1) {
            id
            name
            slug
            book_series(
              order_by: {position: asc}
              where: {book: {canonical_id: {_is_null: true}}}
            ) {
              position
              compilation
              book {
                id
                title
                slug
                release_date
                compilation
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

        if (seriesElement.TryGetProperty("book_series", out var bookSeriesElement) &&
            bookSeriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in bookSeriesElement.EnumerateArray())
            {
                try
                {
                    var book = ParseSeriesRosterEntry(entry);
                    if (book is not null)
                    {
                        result.Books.Add(book);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse Hardcover series roster entry for series {SeriesIdOrUrl}", seriesIdOrUrl);
                }
            }
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

        var slug = document.GetPropertyValueOrNull("slug");

        var result = new SeriesSearchResult(id, name)
        {
            SourceUrl = $"{_hardcoverBaseUrl}/series/{slug ?? id}",
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
            SourceUrl = $"{_hardcoverBaseUrl}/series/{slug ?? id}",
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

    private static SeriesExpectedBookResult? ParseSeriesRosterEntry(JsonElement entry)
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

        int? year = null;
        var releaseDate = bookElement.GetPropertyValueOrNull("release_date");
        if (releaseDate is not null &&
            DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            year = parsedDate.Year;
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

        var identifier = slug ?? bookId;

        // Contributors sometimes only tag one of the two records, so either flag being set is
        // enough to treat the entry as a compilation.
        var linkIsCompilation = entry.TryGetProperty("compilation", out var linkCompilationElement) &&
            linkCompilationElement.ValueKind == JsonValueKind.True;
        var bookIsCompilation = bookElement.TryGetProperty("compilation", out var bookCompilationElement) &&
            bookCompilationElement.ValueKind == JsonValueKind.True;

        return new SeriesExpectedBookResult(title)
        {
            Position = position,
            Year = year,
            SourceUrl = identifier is null ? null : $"{_hardcoverBaseUrl}/books/{identifier}",
            IsCompilation = linkIsCompilation || bookIsCompilation,
        };
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

    private async Task<JsonElement> GetBookById(int bookId)
    {
        var query = _bookDetailsQuery.Replace("BOOK_QUERY_PARAM", "$id: Int!")
                                     .Replace("BOOK_QUERY_FILTER", "books_by_pk(id: $id)");

        var responseElement = await ExecuteGraphqlQuery(query, new { id = bookId });
        return responseElement.GetNestedProperty("data", "books_by_pk");
    }

    private async Task<JsonElement> GetBookBySlug(string slug)
    {
        var query = _bookDetailsQuery.Replace("BOOK_QUERY_PARAM", "$slug: String!")
                                     .Replace("BOOK_QUERY_FILTER", "books(where: {slug: {_eq: $slug}}, limit: 1)");

        var responseElement = await ExecuteGraphqlQuery(query, new { slug });
        var booksArray = responseElement.GetNestedProperty("data", "books");

        if (booksArray.ValueKind == JsonValueKind.Array && booksArray.GetArrayLength() > 0)
        {
            return booksArray[0];
        }

        return default;
    }

    private const string _bookDetailsQuery = """
        query GetBook(BOOK_QUERY_PARAM) {
          BOOK_QUERY_FILTER {
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

        var slug = document.GetPropertyValueOrNull("slug");
        var url = $"{_hardcoverBaseUrl}/books/{slug ?? idStr}";

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
            Series = new List<MetadataSeriesSearchResult>(),
            Genres = new List<string>(),
        };
    }

    private async Task<MetadataSearchResult> ParseBookDetails(JsonElement bookElement, string bookUrl)
    {
        string? bookName = null;
        string? subtitle = null;
        try
        {
            var fullTitle = bookElement.GetPropertyValueOrNull("title");
            if (fullTitle is not null)
            {
                var splitTitle = fullTitle.Split(":");
                bookName = splitTitle[0].Trim();
                if (splitTitle.Length > 1)
                {
                    subtitle = string.Join(":", splitTitle.Skip(1)).Trim();
                }
            }

            subtitle ??= bookElement.GetPropertyValueOrNull("subtitle");
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse contributions for {BookUrl}", bookUrl);
        }

        string? imageUrl = null;
        try
        {
            imageUrl = ParseCachedImage(bookElement);
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

            string? position = null;
            if (bs.TryGetProperty("position", out var positionElement))
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

            series.Add(new MetadataSeriesSearchResult(seriesName)
            {
                SeriesPart = position
            });
        }

        return await _bookSeriesMapper.MapBookSeries(series);
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

    private static (int? Id, string? Slug) ParseBookIdentifierFromUrl(string url)
    {
        // Direct numeric ID
        if (int.TryParse(url, out var directId))
        {
            return (directId, null);
        }

        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new Exception($"Could not extract book identifier from Hardcover URL: {url}");
        }

        var lastSegment = segments.Last();

        // Check if the last segment is a numeric ID
        if (int.TryParse(lastSegment, out var pathId))
        {
            return (pathId, null);
        }

        // Otherwise treat the last segment as a slug
        return (null, lastSegment);
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
