using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AudiobookManager.Scraping.Scrapers;

public partial class AudibleScraper : IScraper
{
    private const string _audibleDomain = "audible.com";
    private const string _audibleBaseUrl = $"https://www.{_audibleDomain}";
    private const string _sourceName = "Audible";

    [GeneratedRegex(@"([^-]+)( - )(.+)")]
    private static partial Regex RePersonWithRole();
    [GeneratedRegex(@"^.*audible\..*\/pd\/.+\/([^\?]+).*$")]
    private static partial Regex ReAsin();
    [GeneratedRegex(@".*Book (\d+\.?\d*)")]
    private static partial Regex ReSeriesPart();
    [GeneratedRegex(@"\d{4}")]
    private static partial Regex ReYear();
    [GeneratedRegex(@"^(\d\.?\d?)(?!.*ratings)")]
    private static partial Regex ReRating();
    [GeneratedRegex(@"\(?([\d,]+) ratings\)?")]
    private static partial Regex ReNumRatings();

    private static readonly Dictionary<string, string> _audibleCommonQueryParameters = new()
    {
        ["skip_spell_correction"] = "true",
        ["overrideBaseCountry"] = "true",
        ["ipRedirectOverride"] = "true"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;

    public AudibleScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
    }

    public bool SupportsUrl(string url) => url.Contains(_audibleDomain);

    public bool IsSource(string sourceName) => _sourceName.Equals(sourceName, StringComparison.InvariantCultureIgnoreCase);

    public string SourceName => _sourceName;

    public async Task<IList<BookSearchResult>> Search(string searchTerm)
    {
        var queryParameters = new Dictionary<string, string>(_audibleCommonQueryParameters)
        {
            { "keywords", searchTerm }
        };

        var uri = QueryHelpers.AddQueryString($"{_audibleBaseUrl}/search", queryParameters);
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting search results from Audible, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();

        HtmlParser parser = new();
        var doc = parser.ParseDocument(responseStream);

        var searchResultElements = doc.QuerySelectorAll("li.bc-list-item.productListItem");

        var searchResultTasks = searchResultElements
            .Select(resultElement => ParseAudibleSearchResult(resultElement));

        await Task.WhenAll(searchResultTasks);

        return searchResultTasks
            .Select(task => task.Result)
            .Where(result => result is not null).ToList();
    }

    public async Task<BookSearchResult> GetBookDetails(string bookUrl)
    {
        var uri = QueryHelpers.AddQueryString(bookUrl, _audibleCommonQueryParameters);
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting search results from Audible, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();

        HtmlParser parser = new();
        var doc = parser.ParseDocument(responseStream);

        return await ParseAudibleDetails(doc, bookUrl);
    }

    private async Task<BookSearchResult?> ParseAudibleSearchResult(IElement resultElem)
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

        int? year = null;
        var releaseDateText = ExtractStringFromTagWithPrefix(resultElem, "li.bc-list-item.releaseDateLabel", "Release date:");
        if (releaseDateText is not null)
        {
            var yearText = releaseDateText.Split("-").Last();
            var currentYear = DateTime.UtcNow.Year.ToString().Substring(2);
            var yearPrefix = int.Parse(yearText) <= int.Parse(currentYear) ? "20" : "19";
            var yearStr = $"{yearPrefix}{yearText}";
            year = int.Parse(yearStr);
        }

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

        return new BookSearchResult(link, titleTag.Text().Trim())
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

    private async Task<BookSearchResult> ParseAudibleDetails(IDocument doc, string bookUrl)
    {
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

        var series = await ParseBookSeries(doc.Body);

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

        return new BookSearchResult(bookUrl, title)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = durationText,
            Year = year,
            Language = null,
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

    private static string? ParseAsinFromUrl(string url)
    {
        var match = ReAsin().Match(url);
        if (match is null)
        {
            return null;
        }

        return match.Groups[1].Value;
    }

    private async Task<IList<BookSeriesSearchResult>> ParseBookSeries(IElement? elem)
    {
        var result = new List<BookSeriesSearchResult>();
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
                result.Add(new BookSeriesSearchResult(aTag.Text().Trim()) { SeriesPart = seriesPart });
            }
        }

        return await _bookSeriesMapper.MapBookSeries(result);
    }
}
