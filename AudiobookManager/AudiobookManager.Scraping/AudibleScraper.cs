using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AudiobookManager.Scraping;

public class AudibleScraper : IAudibleScraper
{
    private const string _audibleBaseUrl = "https://www.audible.com";

    private static Regex _rePersonWithRole = new Regex(@"([^-]+)( - )(.+)", RegexOptions.Compiled);

    private static Regex _reAsin = new Regex(@"^.*audible\..*\/pd\/.+\/([^\?]+).*$", RegexOptions.Compiled);
    private static Regex _reSeriesPart = new Regex(@".*Book (\d+\.?\d*)", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;


    public AudibleScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
    }

    public async Task<IList<BookSearchResult>> SearchAudible(string searchTerm)
    {
        var queryParameters = new Dictionary<string, string>()
        {
            ["keywords"] = searchTerm,
            ["skip_spell_correction"] = "true",
            ["overrideBaseCountry"] = "true",
            ["ipRedirectOverride"] = "true"
        };
        var url = $"{_audibleBaseUrl}/search";
        url = QueryHelpers.AddQueryString(url, queryParameters);
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting search results from Audible, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();

        HtmlParser parser = new();
        var doc = parser.ParseDocument(responseStream);

        var searchResultElements = doc.QuerySelectorAll("li.bc-list-item.productListItem");

        return searchResultElements.Select(x => ParseAudibleSearchResult(x)).Where(x => x is not null).ToList();
    }

    private BookSearchResult? ParseAudibleSearchResult(IElement resultElem)
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

        var series = ParseBookSeries(resultElem);

        return new BookSearchResult(link, titleTag.Text().Trim())
        {
            Authors = authors,
            Narrators = narrators ?? new List<Person>(),
            Subtitle = subtitle,
            Duration = durationText,
            Year = year,
            Language = language,
            ImageUrl = imgUrl,
            // TODO: Need to map series based on DB
            Series = series,
            Asin = asin,
        };
    }

    private string? ParseLength(IElement? elem)
    {
        return ExtractStringFromTagWithPrefix(elem, "li.bc-list-item.runtimeLabel", "Length:");
    }

    private IList<Person>? ParsePersons(IElement? elem)
    {
        if (elem is null)
        {
            return null;
        }

        return elem.QuerySelectorAll("a").Select(x => ParsePersonFromString(x.Text())).ToList();
    }

    private Person ParsePersonFromString(string personString)
    {
        var match = _rePersonWithRole.Match(personString);
        if (match.Success)
        {
            return new Person(match.Groups[1].Value.Trim()) { Role = match.Groups[3].Value.Trim() };
        }

        return new Person(personString.Trim());
    }

    private string? ExtractStringFromTagWithPrefix(IElement? elem, string querySelector, string prefix)
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

    private string? ParseAsinFromUrl(string url)
    {
        var match = _reAsin.Match(url);
        if (match is null)
        {
            return null;
        }

        return match.Groups[1].Value;
    }

    private IList<BookSeriesSearchResult> ParseBookSeries(IElement? elem)
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
                if (nextSiblingText is not null)
                {
                    var match = _reSeriesPart.Match(nextSiblingText);
                    if (match.Success)
                    {
                        seriesPart = match.Groups[1].Value.Trim();
                    }
                }
                result.Add(new BookSeriesSearchResult(aTag.Text().Trim()) { SeriesPart = seriesPart });
            }
        }

        return _bookSeriesMapper.MapBookSeries(result);
    }
}
