using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Extensions;
using AudiobookManager.Scraping.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Scraping.Scrapers;
public partial class GoodreadsScraper : IScraper
{
    private const string _goodreadsDomain = "goodreads.com";
    private const string _goodreadsBaseUrl = $"https://www.{_goodreadsDomain}";
    private const string _sourceName = "Goodreads";
    private const int _maxNumGenresToGet = 5;
    private const string _notABookAuthor = "NOT A BOOK";

    private static readonly IList<string> _ignoredAuthorRoles = new List<string> { "illustrator" };
    private static readonly IList<string> _ignoredGenres = new List<string> { "Fiction" };

    [GeneratedRegex(@"(.*_S\w)\d+(_\..*)")]
    private static partial Regex ReImgUrl();
    [GeneratedRegex(@"/book/show/(\d+)")]
    private static partial Regex ReBookId();
    [GeneratedRegex(@"^B0[A-Z0-9]{8}$")]
    private static partial Regex ReAsin();
    [GeneratedRegex(@"\d{4}")]
    private static partial Regex ReDetailsYear();
    [GeneratedRegex(@"by(.+)")]
    private static partial Regex RePublisher();
    [GeneratedRegex(@"([^#]+)#?(.*)")]
    private static partial Regex ReSeries();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex ReHtmlTags();

    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;
    private readonly ILogger<GoodreadsScraper> _logger;

    public GoodreadsScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper, ILogger<GoodreadsScraper> logger, TimeSpan? retryDelay = null)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
        _logger = logger;

        int maxRetries = 5;

        _resiliencePipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions {
            MaxRetryAttempts = maxRetries,
            Delay = retryDelay ?? TimeSpan.FromSeconds(3),
            BackoffType = DelayBackoffType.Constant,
            OnRetry = (args) =>
            {
                _logger.LogWarning("Exception: {exceptionMessage}, Retrying in {sleepDuration}. {attemptNumber}/{maxRetries}", args.Outcome.Exception?.Message, args.RetryDelay, args.AttemptNumber, maxRetries);
                return ValueTask.CompletedTask;
            }
        })
        .Build();
    }

    /// <summary>
    /// Goodreads fronts the extensionless book page (<c>/book/show/{id}</c>) with an AWS WAF challenge
    /// that answers HTTP 202 with an empty body, while the <c>.xml</c> variant serves the same Next.js
    /// page - <c>__NEXT_DATA__</c> included - without it. This returns the URL to actually fetch;
    /// the caller's own URL stays the book's stored source URL. Anything that is not a Goodreads
    /// book page URL is returned untouched.
    /// </summary>
    public static string ToDetailsUrl(string bookUrl)
    {
        if (!ScraperUrl.HasHost(bookUrl, _goodreadsDomain))
        {
            return bookUrl;
        }

        var match = ReBookId().Match(bookUrl);
        return match.Success ? $"{_goodreadsBaseUrl}/book/show/{match.Groups[1].Value}.xml" : bookUrl;
    }

    /// <summary>
    /// A WAF challenge is a "successful" 202 (or carries <c>x-amzn-waf-action</c>) with no page behind
    /// it, so the status code alone cannot be trusted to mean a page arrived.
    /// </summary>
    public static bool IsWafChallenge(HttpResponseMessage response) =>
        response.StatusCode == System.Net.HttpStatusCode.Accepted ||
        response.Headers.Contains("x-amzn-waf-action");

    public async Task<MetadataSearchResult> GetBookDetails(string bookUrl)
    {
        var uri = ToDetailsUrl(bookUrl);
        var httpClient = _httpClientFactory.CreateClient("goodreads");

        return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
        {
            var response = await httpClient.GetAsync(uri, cancellationToken);

            if (IsWafChallenge(response))
            {
                throw new Exception($"Goodreads answered {bookUrl} with a bot-protection challenge (status code: {(int)response.StatusCode}) instead of the book page");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error getting book details from Goodreads, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            HtmlParser parser = new();
            var doc = parser.ParseDocument(responseStream);

            var mainElementOld = doc.QuerySelector("div#topcol");
            if (mainElementOld is not null)
            {
                _logger.LogInformation("{BookUrl} is in old format", bookUrl);
                return await ParseLegacyBookDetails(doc, bookUrl);
            }

            var scriptElementNew = doc.QuerySelector("script[id=__NEXT_DATA__]");
            if (scriptElementNew is not null)
            {
                _logger.LogInformation("{BookUrl} is in new 2022 format", bookUrl);
                return await ParseNewBookJson(scriptElementNew.Text(), bookUrl);
            }

            throw new Exception($"Goodreads returned a page for {bookUrl} that contains no book data, status code: {response.StatusCode}");
        }, new CancellationToken());
    }

    /// <summary>
    /// Autocomplete is the only search: the <c>/search</c> HTML page sits behind the same bot
    /// protection as the book page, so a "fallback" to it only replaced the real error with an
    /// unrelated one. A failure here propagates to the caller.
    /// </summary>
    public async Task<IList<MetadataSearchResult>> Search(string searchTerm)
    {
        var httpClient = _httpClientFactory.CreateClient("goodreads");
        return await SearchViaAutocomplete(httpClient, searchTerm);
    }

    private async Task<IList<MetadataSearchResult>> SearchViaAutocomplete(HttpClient httpClient, string searchTerm)
    {
        var uri = QueryHelpers.AddQueryString($"{_goodreadsBaseUrl}/book/auto_complete", new Dictionary<string, string?>
        {
            ["format"] = "json",
            ["q"] = searchTerm
        });

        var hits = await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
        {
            var response = await httpClient.GetAsync(uri, cancellationToken);

            if (IsWafChallenge(response) || !response.IsSuccessStatusCode)
            {
                throw new Exception($"Autocomplete API returned status {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseAutocompleteHits(json);
        }, new CancellationToken());

        var results = new List<MetadataSearchResult>();
        foreach (var hit in hits)
        {
            results.Add(await ToSearchResult(hit));
        }

        return results;
    }

    private async Task<MetadataSearchResult> ToSearchResult(AutocompleteHit hit)
    {
        var series = new List<MetadataSeriesSearchResult>();
        if (hit.SeriesName is not null)
        {
            series.Add(new MetadataSeriesSearchResult(hit.SeriesName) { SeriesPart = hit.SeriesPart });
        }

        return new MetadataSearchResult($"{_goodreadsBaseUrl}/book/show/{hit.BookId}", hit.Title)
        {
            Authors = string.IsNullOrEmpty(hit.AuthorName) ? new List<Person>() : new List<Person> { new Person(hit.AuthorName) },
            Narrators = new List<Person>(),
            ImageUrl = ConvertImgUrl(hit.ImageUrl),
            Genres = new List<string>(),
            Series = await _bookSeriesMapper.MapBookSeries(series),
            Rating = hit.Rating,
            NumberOfRatings = hit.NumberOfRatings,
            HasDescription = hit.HasDescription,
        };
    }

    private sealed record AutocompleteHit(
        string BookId,
        string Title,
        string? AuthorName,
        string? ImageUrl,
        string? SeriesName,
        string? SeriesPart,
        float? Rating,
        int? NumberOfRatings,
        bool HasDescription);

    /// <summary>
    /// Turns an autocomplete response into usable hits. Rows without a book id or title are dropped,
    /// as are the ones that are not a book a person would want: the placeholder author
    /// <c>NOT A BOOK</c> Goodreads gives non-book listings, and large-print editions. The title is
    /// <c>bookTitleBare</c> when the hit has one - the plain <c>title</c> carries a
    /// <c>(Series, #N)</c> suffix, which would otherwise end up in the book name - and the series
    /// that suffix names is reported as the hit's series.
    /// </summary>
    private static List<AutocompleteHit> ParseAutocompleteHits(string json)
    {
        var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);
        var hits = new List<AutocompleteHit>();
        if (rows is null)
        {
            return hits;
        }

        foreach (var r in rows)
        {
            if (r.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var bookId = StringOrNull(r, "bookId");
            var fullTitle = StringOrNull(r, "title");
            if (string.IsNullOrWhiteSpace(bookId) || fullTitle is null)
            {
                continue;
            }

            string? authorName = null;
            if (r.TryGetProperty("author", out var authorElement) && authorElement.ValueKind == JsonValueKind.Object)
            {
                authorName = StringOrNull(authorElement, "name");
            }

            if (string.Equals(authorName?.Trim(), _notABookAuthor, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (strippedTitle, seriesName, seriesPart) = SplitSeriesSuffix(fullTitle);
            var bareTitle = StringOrNull(r, "bookTitleBare");
            var title = string.IsNullOrWhiteSpace(bareTitle) ? strippedTitle : bareTitle.Trim();

            if (fullTitle.Contains("Large Print", StringComparison.OrdinalIgnoreCase) || title.Contains("Large Print", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float? rating = null;
            if (StringOrNull(r, "avgRating") is { } avgRating &&
                float.TryParse(avgRating, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedRating))
            {
                rating = parsedRating;
            }

            int? numberOfRatings = null;
            if (r.TryGetProperty("ratingsCount", out var ratingsCountElement) &&
                ratingsCountElement.ValueKind == JsonValueKind.Number &&
                ratingsCountElement.TryGetInt32(out var parsedCount))
            {
                numberOfRatings = parsedCount;
            }

            hits.Add(new AutocompleteHit(bookId, title, authorName, StringOrNull(r, "imageUrl"), seriesName, seriesPart, rating, numberOfRatings, AutocompleteHasDescription(r)));
        }

        return hits;
    }

    private static string? StringOrNull(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// Splits a trailing <c>(Series, #N)</c> off a Goodreads title (<c>61 Hours (Jack Reacher, #14)</c>).
    /// The group is only a series when it holds a <c>#</c>; a title that merely ends in parentheses
    /// (<c>The Hobbit (Illustrated)</c>) is returned whole. Nested parentheses are balanced from the
    /// end, so <c>Title (Series (Omnibus), #2)</c> splits at the right bracket.
    /// </summary>
    public static (string Title, string? SeriesName, string? SeriesPart) SplitSeriesSuffix(string title)
    {
        var trimmed = title.Trim();
        if (!trimmed.EndsWith(')'))
        {
            return (trimmed, null, null);
        }

        var depth = 0;
        var open = -1;
        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            if (trimmed[i] == ')')
            {
                depth++;
            }
            else if (trimmed[i] == '(' && --depth == 0)
            {
                open = i;
                break;
            }
        }

        if (open <= 0)
        {
            return (trimmed, null, null);
        }

        var inner = trimmed[(open + 1)..^1];
        var hash = inner.LastIndexOf('#');
        if (hash < 0)
        {
            return (trimmed, null, null);
        }

        var name = inner[..hash].Trim().TrimEnd(',').Trim();
        var part = inner[(hash + 1)..].Trim();
        if (name.Length == 0 || part.Length == 0)
        {
            return (trimmed, null, null);
        }

        return (trimmed[..open].Trim(), name, part);
    }

    /// <summary>
    /// The autocomplete hit carries a (truncated) <c>description: { html }</c> and omits the key for
    /// a book without one - confirmed live - so an absent or blank value is a definite "missing".
    /// Only the presence is kept: the truncated text must never be mistaken for the real description.
    /// </summary>
    public static bool AutocompleteHasDescription(JsonElement hit) =>
        hit.TryGetProperty("description", out var description) &&
        description.ValueKind == JsonValueKind.Object &&
        !string.IsNullOrWhiteSpace(description.GetPropertyValueOrNull("html"));

    public bool IsSource(string sourceName) => string.Equals(sourceName, _sourceName, StringComparison.InvariantCultureIgnoreCase);

    public bool SupportsUrl(string url) => ScraperUrl.HasHost(url, _goodreadsDomain);

    public string SourceName => _sourceName;

    public async Task<MetadataSearchResult> ParseNewBookJson(string newBookJson, string bookUrl)
    {
        (var bookElement, var workElement, var contributorElements, var seriesElements) = GetBookJsonElements(newBookJson);

        IList<Person> authors = new List<Person>();
        IList<Person> narrators = new List<Person>();
        try
        {
            var allPersons = ParseJsonPersons(bookElement, contributorElements);
            narrators = allPersons.Where(x => string.Equals(x.Role, "Narrator", StringComparison.InvariantCultureIgnoreCase)).ToList();
            authors = FilterAuthors(allPersons.Except(narrators));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse contributors for {BookUrl}", bookUrl);
        }

        string? bookName = null;
        string? subtitle = null;
        try
        {
            // Goodreads exposes no dedicated subtitle field here, and a title's own colon is not
            // reliably a "Title: Subtitle" separator (e.g. "4:50 from Paddington") - so the raw
            // title is kept whole. Recovering a subtitle from it is an explicit, opt-in choice at
            // apply time (TitleSplitter), not a guess baked in at scrape time.
            var bookTitle = bookElement.GetPropertyValueOrNull("title");
            if (bookTitle is not null)
            {
                bookName = bookTitle.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse title for {BookUrl}", bookUrl);
        }

        string? imgUrl = null;
        try
        {
            imgUrl = bookElement.GetPropertyValueOrNull("imageUrl");
            imgUrl = await ValidateCoverUrl(imgUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse image URL for {BookUrl}", bookUrl);
        }

        int? year = null;
        try
        {
            var publicationTimeElementWork = workElement.GetNestedProperty("details", "publicationTime");
            var publicationTimeElementBook = bookElement.GetNestedProperty("details", "publicationTime");
            if (publicationTimeElementWork.ValueKind == JsonValueKind.Number)
            {
                var publishUnixMsTimestamp = publicationTimeElementWork.GetInt64();
                var publicationDateTime = DateTime.UnixEpoch.AddMilliseconds(publishUnixMsTimestamp);
                year = publicationDateTime.Year;
            } else if (publicationTimeElementBook.ValueKind == JsonValueKind.Number)
            {
                var publishUnixMsTimestamp = publicationTimeElementBook.GetInt64();
                var publicationDateTime = DateTime.UnixEpoch.AddMilliseconds(publishUnixMsTimestamp);
                year = publicationDateTime.Year;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse publication year for {BookUrl}", bookUrl);
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

        ParsedRating? ratingResult = null;
        try
        {
            ratingResult = ParseRating(workElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse rating for {BookUrl}", bookUrl);
        }

        IList<MetadataSeriesSearchResult> series = new List<MetadataSeriesSearchResult>();
        try
        {
            series = await ParseSeries(bookElement, seriesElements);
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
            publisher = bookElement.GetNestedProperty("details").GetPropertyValueOrNull("publisher");

            if (bookElement.TryGetProperty("details", out var detailsElement) &&
                detailsElement.TryGetProperty("language", out var languageElement))
            {
                language = languageElement.GetPropertyValueOrNull("name");
            }

            isbn = bookElement.GetNestedProperty("details").GetPropertyValueOrNull("isbn13")
                ?? bookElement.GetNestedProperty("details").GetPropertyValueOrNull("isbn");

            asin = ValidAsinOrNull(bookElement.GetNestedProperty("details").GetPropertyValueOrNull("asin"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse publisher/language/isbn/asin for {BookUrl}", bookUrl);
        }

        return new MetadataSearchResult(bookUrl, bookName ?? string.Empty)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = null,
            Year = year,
            Language = language,
            ImageUrl = imgUrl,
            Series = series,
            Description = description,
            Genres = genres,
            Rating = ratingResult?.Rating,
            NumberOfRatings = ratingResult?.NumberOfRatings,
            Copyright = null,
            Publisher = publisher,
            Asin = asin,
            Isbn = isbn,
        };
    }

    private static (JsonElement BookElement, JsonElement WorkElement, List<JsonElement> ContributorElements, List<JsonElement> SeriesElements) GetBookJsonElements(string jsonText)
    {

        var jsonObject = JsonSerializer.Deserialize<JsonElement>(jsonText);

        var apolloStateElement = jsonObject.GetNestedProperty("props", "pageProps", "apolloState");

        var contributorElements = new List<JsonElement>();
        var seriesElements = new List<JsonElement>();
        JsonElement? maybeBookElement = null;
        JsonElement? workElement = null;


        foreach (var jsonProperty in apolloStateElement.EnumerateObject())
        {
            if (jsonProperty.Name.StartsWith("Contributor"))
            {
                contributorElements.Add(jsonProperty.Value);
            }

            if (jsonProperty.Name.StartsWith("Series"))
            {
                seriesElements.Add(jsonProperty.Value);
            }

            if (jsonProperty.Name.StartsWith("Book") && jsonProperty.Value.TryGetProperty("title", out var _))
            {
                maybeBookElement = jsonProperty.Value;
            }

            if (jsonProperty.Name.StartsWith("Work"))
            {
                workElement = jsonProperty.Value;
            }
        }

        if (maybeBookElement is null)
        {
            throw new Exception("Missing book element in JSON");
        }

        if (workElement is null)
        {
            throw new Exception("Missing work element in JSON");
        }

        return (BookElement: maybeBookElement.Value, WorkElement: workElement.Value, ContributorElements: contributorElements, SeriesElements: seriesElements);
    }

    private IList<string> ParseGenres(JsonElement bookElement)
    {
        if (bookElement.TryGetProperty("bookGenres", out var bookGenresElement))
        {
            return FilterGenres(bookGenresElement.EnumerateArray().Select(e => e.GetNestedProperty("genre", "name").GetString())
                .Where(x => !string.IsNullOrEmpty(x))
                .Cast<string>()
                .ToList());
        }

        return new List<string>();
    }

    private static ParsedRating ParseRating(JsonElement workElement)
    {
        return new ParsedRating
        {
            Rating = workElement.GetNestedProperty("stats", "averageRating").GetSingle(),
            NumberOfRatings = workElement.GetNestedProperty("stats", "ratingsCount").GetInt32()
        };
    }

    private async Task<IList<MetadataSeriesSearchResult>> ParseSeries(JsonElement bookElement, IList<JsonElement> seriesElements)
    {
        var seriesPositions = bookElement
            .GetProperty("bookSeries")
            .EnumerateArray()
            .Select(e => (Position: e.GetPropertyValueOrNull("userPosition"), Ref: e.GetNestedProperty("series", "__ref").GetString()?.Substring(7)))
            .Where(x => x.Ref is not null)
            .Cast<(string? Position, string Ref)>();

        var seriesTitles = seriesElements
            .Select(x => (Id: x.GetPropertyValueOrNull("id"), Title: x.GetPropertyValueOrNull("title")))
            .Where(x => x.Id is not null && x.Title is not null)
            .Cast<(string Id, string Title)>();

        var seriesMap = seriesTitles.ToDictionary(x => x.Id, x => x.Title);

        var series = seriesPositions.Select(x =>
        {
            if (seriesMap.TryGetValue(x.Ref, out var result))
            {
                return new MetadataSeriesSearchResult(result)
                {
                    SeriesPart = x.Position
                };
            }

            return null;
        })
            .Where(x => x is not null)
            .Cast<MetadataSeriesSearchResult>()
            .ToList();

        return await _bookSeriesMapper.MapBookSeries(series);
    }

    private async Task<MetadataSearchResult> ParseLegacyBookDetails(IHtmlDocument doc, string bookUrl)
    {
        var mainElem = doc.QuerySelector("div#topcol")!;
        var allAuthors = ParseAuthors(mainElem);
        var narrators = allAuthors.Where(x => string.Equals(x.Role, "Narrator", StringComparison.InvariantCultureIgnoreCase)).ToList();
        var authors = FilterAuthors(allAuthors.Except(narrators));

        string? bookName = null;
        string? subtitle = null;
        // See the identical comment in ParseNewBookJson: no dedicated subtitle field on this page
        // either, and splitting on a bare colon mis-splits titles like "4:50 from Paddington".
        if (mainElem.TryGetTextFromQuerySelector("h1#bookTitle", out var rawTitleText))
        {
            bookName = rawTitleText.Trim();
        }

        string? imgUrl = null;
        var imgTag = mainElem.QuerySelector("img#coverImage");
        if (imgTag is not null)
        {
            imgUrl = ConvertImgUrl(imgTag.Attributes["src"]?.Value);
        }
        imgUrl = await ValidateCoverUrl(imgUrl);

        var (year, publisher) = ParsePublisherTag(mainElem);

        var description = ParseDescription(mainElem);

        ParsedRating? ratingResult = null;
        var ratingTag = mainElem.QuerySelector("span[itemprop='ratingValue']");
        if (ratingTag is not null)
        {
            ratingResult = ParseOldDetailsRating(ratingTag);
        }

        var bookSeries = await ParseBookSeries(mainElem);

        var genres = ParseGenres(doc);

        return new MetadataSearchResult(bookUrl, bookName ?? string.Empty)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = null,
            Year = year,
            Language = null,
            ImageUrl = imgUrl,
            Series = bookSeries,
            Description = description,
            Genres = genres,
            Rating = ratingResult?.Rating,
            NumberOfRatings = ratingResult?.NumberOfRatings,
            Copyright = null,
            Publisher = publisher,
            Asin = null,
        };
    }

    private static IList<Person> FilterAuthors(IEnumerable<Person> persons)
    {
        return persons
            .Where(p => p.Role is null || !_ignoredAuthorRoles.Any(ignoredRole => string.Equals(ignoredRole, p.Role, StringComparison.InvariantCultureIgnoreCase)))
            .ToList();
    }

    private static IList<Person> ParseJsonPersons(JsonElement bookElement, IList<JsonElement> contributorElements)
    {
        var contributorEdges = new List<JsonElement>();

        if (bookElement.TryGetProperty("primaryContributorEdge", out var primContributorElement))
        {
            contributorEdges.Add(primContributorElement);
        }

        if (bookElement.TryGetProperty("secondaryContributorEdges", out var secondaryContributorElement))
        {
            contributorEdges.AddRange(secondaryContributorElement.EnumerateArray());
        }

        var contributorRoleMap = contributorEdges.Select(x => new { Ref = x.GetNestedProperty("node", "__ref").GetString()?.Substring(12), Role = x.GetProperty("role").GetString() });

        var contributorNameMap = contributorElements
            .Where(x => x.GetProperty("id").GetString() is not null)
            .ToDictionary(x => x.GetProperty("id").GetString()!, x => x.GetPropertyValueOrNull("name"));

        return contributorRoleMap.Select(x =>
        {
            if (x.Ref is not null && contributorNameMap.TryGetValue(x.Ref, out var result) && result is not null)
            {
                return new Person(result)
                {
                    Role = x.Role
                };
            }

            return null;
        })
            .Where(p => p is not null)
            .Cast<Person>()
            .ToList();
    }

    private static IList<string> FilterGenres(IEnumerable<string> genres)
    {
        return genres
            .Where(genreText => _ignoredGenres.All(ignoredGenre => !string.Equals(ignoredGenre, genreText, StringComparison.InvariantCultureIgnoreCase)))
            .Take(_maxNumGenresToGet)
            .ToList();
    }

    private static IList<string> ParseGenres(IHtmlDocument doc)
    {
        var genres = new List<string>();
        var genreHeaderTag = doc.QuerySelectorAll("h2.brownBackground").ToList().FirstOrDefault(x => x.Text().Trim() == "Genres");

        if (genreHeaderTag is not null)
        {
            var bigBoxTag = genreHeaderTag.ParentElement?.ParentElement;
            var genreElementTags = bigBoxTag?.QuerySelectorAll("div.bigBoxBody div.elementList");
            if (genreElementTags is not null)
            {
                foreach (var genreElementTag in genreElementTags.ToList())
                {
                    var genreATags = genreElementTag.QuerySelectorAll("div.left>a.bookPageGenreLink");
                    if (genreATags is not null)
                    {
                        genres.Add(genreATags.Last().Text().Trim());
                    }
                }
            }
        }

        return FilterGenres(genres);
    }

    private async Task<IList<MetadataSeriesSearchResult>> ParseBookSeries(IElement mainElem)
    {
        var series = new List<MetadataSeriesSearchResult>();

        var dataDivTags = mainElem.QuerySelectorAll("div#bookDataBox>div");
        foreach (var dataDivTag in dataDivTags.ToList())
        {
            var titleTag = dataDivTag.QuerySelector("div.infoBoxRowTitle");
            if (titleTag is not null)
            {
                if (string.Equals(titleTag.Text().Trim(), "Series", StringComparison.InvariantCultureIgnoreCase))
                {
                    var itemATags = dataDivTag.QuerySelectorAll("div.infoBoxRowItem a");
                    foreach (var itemATag in itemATags.ToList())
                    {
                        if (ReSeries().TryMatch(itemATag.Text().Trim(), out var seriesMatch))
                        {
                            series.Add(new MetadataSeriesSearchResult(seriesMatch.Groups[1].Value.Trim())
                            {
                                SeriesPart = seriesMatch.Groups[2].Value
                            });
                        }
                    }
                }
            }
        }

        return await _bookSeriesMapper.MapBookSeries(series);
    }

    private static string? ParseDescription(IElement mainElem)
    {
        var descriptionSpanTags = mainElem.QuerySelectorAll("div#description span");
        if (descriptionSpanTags.Length > 1)
        {
            return descriptionSpanTags[1].Text().Trim();
        }
        else if (descriptionSpanTags.Length == 1)
        {
            return descriptionSpanTags[0].Text().Trim();
        }

        return null;
    }

    private static (int?, string?) ParsePublisherTag(IElement mainElem)
    {
        (int? Year, string? Publisher) result = (null, null);

        var detailsTag = mainElem.QuerySelector("div#details");
        IElement? publisherElem = null;
        if (detailsTag is not null)
        {
            var rowTags = detailsTag.QuerySelectorAll("div.row");
            if (rowTags.Length >= 2)
            {
                publisherElem = rowTags[1];
            }
            else if (rowTags.Length == 1)
            {
                publisherElem = rowTags[0];
            }
        }

        var publisherText = publisherElem?.Text().Trim();
        if (!string.IsNullOrEmpty(publisherText))
        {
            var yearMatches = ReDetailsYear().Matches(publisherText);
            foreach (var yearMatch in yearMatches.ToList())
            {
                var parsedYear = int.Parse(yearMatch.Value);
                if (result.Year is null || parsedYear < result.Year)
                {
                    result.Year = parsedYear;
                }
            }

            if (RePublisher().TryMatch(publisherText, out var publisherMatch))
            {
                result.Publisher = publisherMatch.Groups[1].Value.Trim();
            }
        }

        return result;
    }

    private static ParsedRating ParseOldDetailsRating(IElement ratingElement)
    {
        var parsed = new ParsedRating();

        parsed.Rating = float.Parse(ratingElement.Text().Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

        var numRatingsTagValue = ratingElement.ParentElement?.QuerySelector("meta[itemprop='ratingCount']")?.Attributes["content"]?.Value;
        if (numRatingsTagValue is not null)
        {
            parsed.NumberOfRatings = int.Parse(numRatingsTagValue, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        return parsed;
    }

    /// <summary>
    /// Goodreads' <c>details.asin</c> is not always a Kindle ASIN: print editions carry their ISBN-10
    /// there (<c>0385340583</c>). Only a Kindle ASIN - <c>B0</c> plus eight alphanumerics - is kept,
    /// so an ISBN-10 never ends up in a book's ASIN tag.
    /// </summary>
    public static string? ValidAsinOrNull(string? asin) =>
        asin is not null && ReAsin().IsMatch(asin.Trim()) ? asin.Trim() : null;

    private static string? ConvertImgUrl(string? imgUrl)
    {
        if (imgUrl is null)
        {
            return null;
        }

        var imgUrlMatch = ReImgUrl().Match(imgUrl);
        if (imgUrlMatch.Success)
        {
            return $"{imgUrlMatch.Groups[1].Value}1000{imgUrlMatch.Groups[2].Value}";
        }

        return imgUrl;
    }

    private static IList<Person> ParseAuthors(IElement resultElem)
    {
        var authors = new List<Person>();

        foreach (var authorTag in resultElem.QuerySelectorAll("div.authorName__container"))
        {
            var authorName = authorTag.QuerySelector("a.authorName")?.Text().Trim();
            if (!string.IsNullOrEmpty(authorName))
            {
                var author = new Person(authorName);

                if (resultElem.TryGetTextFromQuerySelector("span.role", out var roleText))
                {
                    author.Role = roleText.Replace("(", "").Replace(")", "");
                }

                authors.Add(author);
            }
        }

        return authors;
    }

    private async Task<string?> ValidateCoverUrl(string? imgUrl)
    {
        if (string.IsNullOrEmpty(imgUrl))
        {
            return imgUrl;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient("goodreads");
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var request = new HttpRequestMessage(HttpMethod.Head, imgUrl);
            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode &&
                response.Content.Headers.ContentLength is long contentLength &&
                contentLength <= 1000)
            {
                _logger.LogInformation("Cover URL {ImgUrl} appears to be a placeholder image ({ContentLength} bytes), ignoring", imgUrl, contentLength);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate cover URL {ImgUrl}, keeping as-is", imgUrl);
        }

        return imgUrl;
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

        result = ReHtmlTags().Replace(result, "");

        result = result
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"");

        return result.Trim();
    }

    private class PublisherParseResult
    {
        public int? Year { get; set; }
        public string? Publisher { get; set; }
    }
}
