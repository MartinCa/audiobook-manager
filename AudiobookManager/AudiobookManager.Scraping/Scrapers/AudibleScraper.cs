using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Scraping.Scrapers;

public partial class AudibleScraper : IScraper
{
    private const string _audibleDomain = "audible.com";
    private const string _audibleBaseUrl = $"https://www.{_audibleDomain}";
    private const string _sourceName = "Audible";

    [GeneratedRegex(@"([^-]+)( - )(.+)")]
    private static partial Regex RePersonWithRole();
    [GeneratedRegex(@"^.*audible\..*\/pd\/(?:[^\/\?]+\/)?([^\/\?]+)")]
    private static partial Regex ReAsin();
    [GeneratedRegex(@"^.*audible\..*\/series\/.+\/([^\?]+).*$")]
    private static partial Regex ReSeriesId();
    [GeneratedRegex(@".*Book (\d+\.?\d*)")]
    private static partial Regex ReSeriesPart();
    // Anchored (unlike ReSeriesPart above) so it only matches a series page's roster heading
    // ("Book 1", "Book 15.5") and not a book title that merely contains the word "Book".
    [GeneratedRegex(@"^Book (\d+\.?\d*)$")]
    private static partial Regex ReSeriesPositionHeading();
    [GeneratedRegex(@"\d{4}")]
    private static partial Regex ReYear();
    [GeneratedRegex(@"^(\d\.?\d?)(?!.*ratings)")]
    private static partial Regex ReRating();
    [GeneratedRegex(@"\(?([\d,]+) ratings\)?")]
    private static partial Regex ReNumRatings();

    private static readonly Dictionary<string, string?> _audibleCommonQueryParameters = new()
    {
        ["skip_spell_correction"] = "true",
        ["overrideBaseCountry"] = "true",
        ["ipRedirectOverride"] = "true"
    };

    // Safety cap on GetSeriesBooks' page loop - well above any real series (Jack Reacher, one
    // of Audible's longest-running, is ~8 pages), so this only guards against a pagination
    // end-of-list detection bug rather than a real series ever hitting the limit.
    private const int _maxSeriesPages = 60;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;
    private readonly ILogger<AudibleScraper> _logger;

    public AudibleScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper, ILogger<AudibleScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
        _logger = logger;
    }

    public bool SupportsUrl(string url) => ScraperUrl.HasHost(url, _audibleDomain);

    public bool IsSource(string sourceName) => _sourceName.Equals(sourceName, StringComparison.InvariantCultureIgnoreCase);

    public string SourceName => _sourceName;

    public async Task<IList<MetadataSearchResult>> Search(string searchTerm)
    {
        var doc = await FetchSearchDocument(searchTerm);

        var searchResultElements = doc.QuerySelectorAll("li.bc-list-item.productListItem");

        var searchResultTasks = searchResultElements
            .Select(resultElement => ParseAudibleSearchResult(resultElement))
            .ToList();

        await Task.WhenAll(searchResultTasks);

        return searchResultTasks
            .Select(task => task.Result)
            .OfType<MetadataSearchResult>().ToList();
    }

    public bool SupportsSeriesLookup => true;

    /// <summary>
    /// Audible has no series-only search endpoint we've found, so this runs the same book
    /// search as <see cref="Search"/> and surfaces the distinct series referenced by each
    /// hit's "Series:" link (the same markup <see cref="ParseBookSeriesFromLegacyMarkup"/>
    /// reads for a single book), deduplicated by series id across all hits.
    /// </summary>
    public async Task<IList<SeriesSearchResult>> SearchSeries(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new List<SeriesSearchResult>();
        }

        var doc = await FetchSearchDocument(searchTerm);

        var seriesById = new Dictionary<string, SeriesSearchResult>();
        foreach (var resultElem in doc.QuerySelectorAll("li.bc-list-item.productListItem"))
        {
            var seriesTag = resultElem.QuerySelector("li.bc-list-item.seriesLabel");
            if (seriesTag is null)
            {
                continue;
            }

            var authors = resultElem.QuerySelector("li.bc-list-item.authorLabel")?
                .QuerySelectorAll("a").Select(a => a.Text().Trim()).ToList()
                ?? new List<string>();

            foreach (var aTag in seriesTag.QuerySelectorAll("a"))
            {
                var seriesName = aTag.Text().Trim();
                var href = aTag.Attributes["href"]?.Value;
                if (string.IsNullOrEmpty(seriesName) || string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var absoluteUrl = ResolveAbsoluteUrl(href);
                var seriesId = ParseSeriesIdFromUrl(absoluteUrl);
                if (seriesId is null || seriesById.ContainsKey(seriesId))
                {
                    continue;
                }

                seriesById[seriesId] = new SeriesSearchResult(seriesId, seriesName)
                {
                    SourceUrl = absoluteUrl.Split('?')[0],
                    Authors = authors,
                };
            }
        }

        return seriesById.Values.ToList();
    }

    /// <summary>
    /// Fetches a series' full book roster by paging through its Audible series page
    /// (?page=1, 2, ...) until the "next" button is absent or disabled. Beware: each numbered
    /// position ("Book 1") is immediately followed on the page by one or more sibling entries
    /// for alternate editions/narrations of the same book - those share the position's cover
    /// art and metadata shape but their own heading is the book's title, not "Book N", so only
    /// entries whose heading matches <see cref="ReSeriesPositionHeading"/> are kept; this also
    /// drops bonus/spin-off entries (companion books, "Stories Behind The Stories" featurettes)
    /// that never carry a position at all. The "N books in series" figure shown on the page
    /// counts every one of those entries (editions included), not unique series positions, so
    /// it is deliberately not used as BookCount - the roster's own count is used instead.
    /// </summary>
    public async Task<SeriesSearchResult?> GetSeriesBooks(string seriesIdOrUrl)
    {
        var basePath = ResolveSeriesBasePath(seriesIdOrUrl);
        if (basePath is null)
        {
            _logger.LogWarning("Could not resolve an Audible series URL from {SeriesIdOrUrl}", seriesIdOrUrl);
            return null;
        }

        SeriesSearchResult? result = null;
        var seenPositions = new HashSet<string>();

        for (var page = 1; page <= _maxSeriesPages; page++)
        {
            var pageQueryParameters = new Dictionary<string, string?>(_audibleCommonQueryParameters)
            {
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
            };
            var pageUri = QueryHelpers.AddQueryString(basePath, pageQueryParameters);
            var doc = await FetchAudibleDocument(pageUri);

            if (result is null)
            {
                var seriesName = doc.QuerySelector("h1[data-testid='series-title']")?.Text().Trim();
                if (string.IsNullOrEmpty(seriesName))
                {
                    return null;
                }

                var canonicalUrl = doc.QuerySelector("link[rel='canonical']")?.Attributes["href"]?.Value ?? pageUri;
                var seriesId = ParseSeriesIdFromUrl(canonicalUrl) ?? seriesIdOrUrl;

                result = new SeriesSearchResult(seriesId, seriesName)
                {
                    SourceUrl = canonicalUrl,
                };
            }

            var addedAny = false;
            foreach (var itemElem in doc.QuerySelectorAll("li.bc-list-item.productListItem"))
            {
                var book = ParseSeriesRosterEntry(itemElem);
                if (book?.Position is null || !seenPositions.Add(book.Position))
                {
                    continue;
                }

                result.Books.Add(book);
                addedAny = true;
            }

            var nextLink = doc.QuerySelector("span.nextButton a");
            var nextIsDisabled = nextLink is null ||
                string.Equals(nextLink.Attributes["aria-disabled"]?.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (nextIsDisabled)
            {
                break;
            }

            if (!addedAny)
            {
                _logger.LogWarning(
                    "Audible series page {Page} for {SeriesIdOrUrl} added no new roster entries but reported a next page - stopping pagination defensively",
                    page, seriesIdOrUrl);
                break;
            }
        }

        if (result is not null)
        {
            result.BookCount = result.Books.Count;
        }

        return result;
    }

    private Task<IDocument> FetchSearchDocument(string searchTerm)
    {
        var queryParameters = new Dictionary<string, string?>(_audibleCommonQueryParameters)
        {
            { "keywords", searchTerm }
        };

        var uri = QueryHelpers.AddQueryString($"{_audibleBaseUrl}/search", queryParameters);
        return FetchAudibleDocument(uri);
    }

    private async Task<IDocument> FetchAudibleDocument(string uri)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting page from Audible, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();

        HtmlParser parser = new();
        return parser.ParseDocument(responseStream);
    }

    private string ResolveAbsoluteUrl(string hrefOrUrl) =>
        hrefOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? hrefOrUrl : $"{_audibleBaseUrl}{hrefOrUrl}";

    /// <summary>
    /// Accepts a bare Audible series id (as returned in <see cref="SeriesSearchResult.SourceId"/>)
    /// or a full series URL. A bare id has no slug to build the canonical URL from, but Audible
    /// accepts any placeholder slug segment and 301s to the real one, so "/series/-/{id}" still
    /// resolves - confirmed against the live site rather than assumed.
    /// </summary>
    private string? ResolveSeriesBasePath(string seriesIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(seriesIdOrUrl))
        {
            return null;
        }

        if (Uri.TryCreate(seriesIdOrUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        return $"{_audibleBaseUrl}/series/-/{Uri.EscapeDataString(seriesIdOrUrl.Trim())}";
    }

    private static string? ParseSeriesIdFromUrl(string url)
    {
        var match = ReSeriesId().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Parses one series-page roster row. Returns null for rows that aren't a numbered series
    /// entry - see the remarks on <see cref="GetSeriesBooks"/> for why those exist on the page.
    /// </summary>
    private SeriesExpectedBookResult? ParseSeriesRosterEntry(IElement itemElem)
    {
        var headingText = itemElem.QuerySelector("h2.bc-heading")?.Text().Trim();
        if (string.IsNullOrEmpty(headingText))
        {
            return null;
        }

        var positionMatch = ReSeriesPositionHeading().Match(headingText);
        if (!positionMatch.Success)
        {
            return null;
        }

        var titleTag = itemElem.QuerySelector("h3 a");
        var title = itemElem.Attributes["aria-label"]?.Value?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = titleTag?.Text().Trim();
        }
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var href = titleTag?.Attributes["href"]?.Value;
        var sourceUrl = string.IsNullOrEmpty(href) ? null : ResolveAbsoluteUrl(href);

        var releaseDateText = ExtractStringFromTagWithPrefix(itemElem, "li.bc-list-item.releaseDateLabel", "Release date:");

        return new SeriesExpectedBookResult(title)
        {
            Position = positionMatch.Groups[1].Value,
            Year = ParseYearFromReleaseDateText(releaseDateText),
            SourceUrl = sourceUrl,
            IsCompilation = false,
        };
    }

    public async Task<MetadataSearchResult> GetBookDetails(string bookUrl)
    {
        var uri = QueryHelpers.AddQueryString(bookUrl, _audibleCommonQueryParameters);
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting search results from Audible, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var html = await response.Content.ReadAsStringAsync();

        return await ParseAudibleDetails(html, bookUrl);
    }

    private async Task<MetadataSearchResult?> ParseAudibleSearchResult(IElement resultElem)
    {
        var titleTag = resultElem.QuerySelector("h3 a");

        if (titleTag is null)
        {
            return null;
        }

        var link = $"{_audibleBaseUrl}{titleTag?.Attributes["href"]?.Value}";

        var subtitle = "";
        var subtitleTag = resultElem.QuerySelector("li.bc-list-item.subtitle");
        if (subtitleTag is not null)
        {
            subtitle = subtitleTag.Text().Trim();
        }

        var authors = ParsePersons(resultElem.QuerySelector("li.bc-list-item.authorLabel"));

        if (authors is null)
        {
            return null;
        }

        var narrators = ParsePersons(resultElem.QuerySelector("li.bc-list-item.narratorLabel"));

        var durationText = ParseLength(resultElem);

        var releaseDateText = ExtractStringFromTagWithPrefix(resultElem, "li.bc-list-item.releaseDateLabel", "Release date:");
        var year = ParseYearFromReleaseDateText(releaseDateText);

        var language = ExtractStringFromTagWithPrefix(resultElem, "li.bc-list-item.languageLabel", "Language:");

        string? imgUrl = null;
        var imageTag = resultElem.QuerySelector("img");
        if (imageTag is not null)
        {
            imgUrl = imageTag.Attributes["src"]?.Value;
        }

        var asin = ParseAsinFromUrl(link);

        var series = await ParseBookSeries(resultElem);

        var ratingResult = ParseRating(resultElem);

        return new MetadataSearchResult(link, titleTag!.Text().Trim())
        {
            Authors = authors,
            Narrators = narrators ?? new List<Person>(),
            Subtitle = subtitle,
            Duration = durationText,
            Year = year,
            Language = language,
            ImageUrl = imgUrl,
            Series = series,
            Description = null,
            Genres = new List<string>(),
            Rating = ratingResult.Rating,
            NumberOfRatings = ratingResult.NumberOfRatings,
            Copyright = null,
            Publisher = null,
            Asin = asin,
        };
    }

    public async Task<MetadataSearchResult> ParseAudibleDetails(string html, string bookUrl)
    {
        HtmlParser parser = new();
        var doc = parser.ParseDocument(html);

        var audiobookJson = FindLdJsonObject(doc, "Audiobook");

        if (audiobookJson is not { } audiobook)
        {
            throw new Exception("Could not parse book details from Audible page");
        }

        var title = GetJsonString(audiobook, "name")?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new Exception("Could not parse title");
        }

        var subtitle = doc.QuerySelector("adbl-title-lockup h2[slot='subtitle']")?.Text().Trim();
        subtitle = string.IsNullOrEmpty(subtitle) ? null : subtitle;

        var authors = ParsePersonsFromJson(GetJsonProperty(audiobook, "author"));

        if (authors.Count == 0)
        {
            throw new Exception("Could not parse authors");
        }

        var narrators = ParsePersonsFromJson(GetJsonProperty(audiobook, "readBy"));

        var durationText = ParseIsoDuration(GetJsonString(audiobook, "duration"));

        var genres = doc.QuerySelectorAll("adbl-chip-group[slot='chips'] adbl-chip")
            .Select(x => x.Text().Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .ToList();

        var seriesResults = ParseBookSeriesFromDetailsJson(doc);
        if (seriesResults.Count == 0)
        {
            seriesResults = ParseBookSeriesFromLegacyMarkup(doc.Body);
        }
        var series = await _bookSeriesMapper.MapBookSeries(seriesResults);

        var imgUrl = GetJsonString(audiobook, "image");

        var description = ParseDescriptionHtml(GetJsonString(audiobook, "description"));

        var publisher = GetJsonString(audiobook, "publisher");

        int? year = null;
        var datePublished = GetJsonString(audiobook, "datePublished");
        if (!string.IsNullOrEmpty(datePublished))
        {
            var yearMatch = ReYear().Match(datePublished);
            if (yearMatch.Success)
            {
                year = int.Parse(yearMatch.Value, CultureInfo.InvariantCulture);
            }
        }

        float? rating = null;
        int? numberOfRatings = null;
        if (GetJsonProperty(audiobook, "aggregateRating") is { ValueKind: JsonValueKind.Object } aggregateRating)
        {
            if (aggregateRating.TryGetProperty("ratingValue", out var ratingValueProp))
            {
                rating = ParseJsonNumber(ratingValueProp);
            }
            if (aggregateRating.TryGetProperty("ratingCount", out var ratingCountProp))
            {
                var ratingCountValue = ParseJsonNumber(ratingCountProp);
                if (ratingCountValue.HasValue)
                {
                    numberOfRatings = (int)ratingCountValue.Value;
                }
            }
        }

        var asin = ParseAsinFromUrl(bookUrl);

        var language = GetJsonString(audiobook, "inLanguage");
        if (string.IsNullOrWhiteSpace(language) && doc.Body is not null)
        {
            language = ExtractStringFromTagWithPrefix(doc.Body, "li.bc-list-item.languageLabel", "Language:");
        }

        return new MetadataSearchResult(bookUrl, title)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = durationText,
            Year = year,
            Language = language,
            ImageUrl = imgUrl,
            Series = series,
            Description = description,
            Genres = genres,
            Rating = rating,
            NumberOfRatings = numberOfRatings,
            Copyright = publisher,
            Publisher = publisher,
            Asin = asin,
        };
    }

    private static JsonElement? FindLdJsonObject(IDocument doc, string typeName)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var text = script.TextContent;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            JsonDocument jsonDoc;
            try
            {
                jsonDoc = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                continue;
            }

            using (jsonDoc)
            {
                var root = jsonDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (MatchesLdType(item, typeName))
                        {
                            return item.Clone();
                        }
                    }
                }
                else if (MatchesLdType(root, typeName))
                {
                    return root.Clone();
                }
            }
        }

        return null;
    }

    private static bool MatchesLdType(JsonElement elem, string typeName)
    {
        return elem.ValueKind == JsonValueKind.Object
            && elem.TryGetProperty("@type", out var typeProp)
            && typeProp.ValueKind == JsonValueKind.String
            && string.Equals(typeProp.GetString(), typeName, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement? GetJsonProperty(JsonElement elem, string name)
    {
        return elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(name, out var prop) ? prop : null;
    }

    private static string? GetJsonString(JsonElement elem, string name)
    {
        var prop = GetJsonProperty(elem, name);
        return prop is { ValueKind: JsonValueKind.String } ? prop.Value.GetString() : null;
    }

    private static float? ParseJsonNumber(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.GetSingle(),
            JsonValueKind.String when float.TryParse(elem.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    private static IList<Person> ParsePersonsFromJson(JsonElement? arrayElem)
    {
        var result = new List<Person>();
        if (arrayElem is not { ValueKind: JsonValueKind.Array } array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            var name = GetJsonString(item, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(ParsePersonFromString(name));
            }
        }

        return result;
    }

    private static string? ParseIsoDuration(string? isoDuration)
    {
        if (string.IsNullOrWhiteSpace(isoDuration))
        {
            return null;
        }

        try
        {
            var timeSpan = XmlConvert.ToTimeSpan(isoDuration);
            var hours = (int)timeSpan.TotalHours;
            var minutes = timeSpan.Minutes;
            return hours > 0 ? $"{hours} hrs and {minutes} mins" : $"{minutes} mins";
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ParseDescriptionHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var parser = new HtmlParser();
        var fragmentDoc = parser.ParseDocument($"<div>{html}</div>");
        var text = fragmentDoc.Body?.Text().Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static ParsedRating ParseRating(IElement mainElem)
    {
        ParsedRating result = new();
        var ratingTag = mainElem.QuerySelector("li.bc-list-item.ratingsLabel");
        if (ratingTag is not null)
        {
            var bcTextTags = ratingTag.QuerySelectorAll("span.bc-text");
            foreach (var bcTextTag in bcTextTags)
            {
                var bcTextTagText = bcTextTag.Text().Trim();
                var ratingMatch = ReRating().Match(bcTextTag.Text().Trim());
                if (ratingMatch.Success)
                {
                    result.Rating = float.Parse(ratingMatch.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                }
                var numRatingsMatch = ReNumRatings().Match(bcTextTagText);
                if (numRatingsMatch.Success)
                {
                    result.NumberOfRatings = int.Parse(numRatingsMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                }
            }
        }

        return result;
    }

    private static string? ParseLength(IElement? elem)
    {
        return ExtractStringFromTagWithPrefix(elem, "li.bc-list-item.runtimeLabel", "Length:");
    }

    private static IList<Person>? ParsePersons(IElement? elem)
    {
        if (elem is null)
        {
            return null;
        }

        return elem.QuerySelectorAll("a").Select(x => ParsePersonFromString(x.Text())).ToList();
    }

    private static Person ParsePersonFromString(string personString)
    {
        var match = RePersonWithRole().Match(personString);
        if (match.Success)
        {
            return new Person(match.Groups[1].Value.Trim()) { Role = match.Groups[3].Value.Trim() };
        }

        return new Person(personString.Trim());
    }

    private static string? ExtractStringFromTagWithPrefix(IElement? elem, string querySelector, string prefix)
    {
        var tag = elem?.QuerySelector(querySelector);
        if (tag is null)
        {
            return null;
        }

        var tagText = tag.Text();
        var prefixIdx = tagText.IndexOf(prefix);
        return tagText.Substring(prefixIdx + prefix.Length).Trim();
    }

    /// <summary>
    /// Audible's release-date tags only give a two-digit year ("10-27-15"); the century is
    /// inferred by comparing against the current two-digit year, same as a credit-card expiry.
    /// </summary>
    private static int? ParseYearFromReleaseDateText(string? releaseDateText)
    {
        if (string.IsNullOrEmpty(releaseDateText))
        {
            return null;
        }

        var yearText = releaseDateText.Split("-").Last();
        if (!int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twoDigitYear))
        {
            return null;
        }

        var currentTwoDigitYear = int.Parse(DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture).Substring(2), CultureInfo.InvariantCulture);
        var yearPrefix = twoDigitYear <= currentTwoDigitYear ? "20" : "19";
        return int.Parse($"{yearPrefix}{yearText}", CultureInfo.InvariantCulture);
    }

    private static string? ParseAsinFromUrl(string url)
    {
        var match = ReAsin().Match(url);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Value;
    }

    private async Task<IList<MetadataSeriesSearchResult>> ParseBookSeries(IElement? elem)
    {
        return await _bookSeriesMapper.MapBookSeries(ParseBookSeriesFromLegacyMarkup(elem));
    }

    private static IList<MetadataSeriesSearchResult> ParseBookSeriesFromLegacyMarkup(IElement? elem)
    {
        var result = new List<MetadataSeriesSearchResult>();
        var seriesTag = elem?.QuerySelector("li.bc-list-item.seriesLabel");
        if (seriesTag is not null)
        {
            var aTags = seriesTag.QuerySelectorAll("a");

            foreach (var aTag in aTags)
            {
                if (aTag is null)
                {
                    continue;
                }

                string? seriesPart = null;
                var nextSiblingText = aTag.NextSibling?.Text();
                if (!string.IsNullOrEmpty(nextSiblingText))
                {
                    var match = ReSeriesPart().Match(nextSiblingText);
                    if (match.Success)
                    {
                        seriesPart = match.Groups[1].Value.Trim();
                    }
                }
                result.Add(new MetadataSeriesSearchResult(aTag.Text().Trim()) { SeriesPart = seriesPart });
            }
        }

        return result;
    }

    /// <summary>
    /// The detail page's series line ("Jack Reacher, Book 1") isn't in the DOM as text/links (unlike the
    /// search-result markup) - it's only present as JSON inside a script tag nested in the
    /// &lt;adbl-product-details&gt; metadata block: {"series":[{"part":"Book 1","name":"Jack Reacher",...}]}.
    /// </summary>
    private static IList<MetadataSeriesSearchResult> ParseBookSeriesFromDetailsJson(IDocument doc)
    {
        var result = new List<MetadataSeriesSearchResult>();

        var script = doc.QuerySelector("adbl-product-details adbl-product-metadata script[type='application/json']");
        var text = script?.TextContent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        JsonDocument jsonDoc;
        try
        {
            jsonDoc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return result;
        }

        using (jsonDoc)
        {
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Object
                && jsonDoc.RootElement.TryGetProperty("series", out var seriesProp)
                && seriesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in seriesProp.EnumerateArray())
                {
                    var name = GetJsonString(item, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var part = GetJsonString(item, "part");
                    string? seriesPart = null;
                    if (!string.IsNullOrEmpty(part))
                    {
                        var match = ReSeriesPart().Match(part);
                        seriesPart = match.Success ? match.Groups[1].Value.Trim() : part.Trim();
                    }

                    result.Add(new MetadataSeriesSearchResult(name.Trim()) { SeriesPart = seriesPart });
                }
            }
        }

        return result;
    }
}
