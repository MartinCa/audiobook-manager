using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AudiobookManager.Scraping;

public class AudibleScraper : IScraper
{
    private const string _audibleBaseUrl = "https://www.audible.com";
    private const string _sourceName = "Audible";

    private static readonly Regex _rePersonWithRole = new Regex(@"([^-]+)( - )(.+)", RegexOptions.Compiled);
    private static readonly Regex _reAsin = new Regex(@"^.*audible\..*\/pd\/.+\/([^\?]+).*$", RegexOptions.Compiled);
    private static readonly Regex _reSeriesPart = new Regex(@".*Book (\d+\.?\d*)", RegexOptions.Compiled);
    private static readonly Regex _reYear = new Regex(@"\d{4}", RegexOptions.Compiled);
    private static readonly Regex _reCopyrightPublisher = new Regex(@"^©\d+(.+)\(P\)\d+(.+)$", RegexOptions.Compiled);
    private static readonly Regex _reRating = new Regex(@"^(\d\.?\d?)(?!.*ratings)", RegexOptions.Compiled);
    private static readonly Regex _reNumRatings = new Regex(@"\(?([\d,]+) ratings\)?", RegexOptions.Compiled);

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

    public bool SupportsUrl(string url) => url.Contains("audible.com");

    public bool IsSource(string sourceName) => _sourceName.Equals(sourceName, StringComparison.InvariantCultureIgnoreCase);

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

        return searchResultElements.Select(x => ParseAudibleSearchResult(x)).Where(x => x is not null).ToList();
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

        var mainElement = doc.QuerySelector("div.adbl-main");

        if (mainElement is null)
        {
            throw new Exception($"Invalid response from Audible, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        return ParseAudibleDetails(mainElement, bookUrl);
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
            NumberOfRatings = ratingResult.NumRatings,
            Copyright = null,
            Publisher = null,
            Asin = asin,
        };
    }

    private BookSearchResult ParseAudibleDetails(IElement mainElem, string bookUrl)
    {
        var titleTag = mainElem.QuerySelector("h1.bc-heading");

        if (titleTag is null)
        {
            throw new Exception("Could not parse title");
        }

        var title = titleTag.Text().Trim();

        var afterTitleTag = titleTag.ParentElement?.NextElementSibling;
        string? subtitle = null;
        if (afterTitleTag is not null && !afterTitleTag.ClassList.Contains("authorLabel"))
        {
            var tagText = afterTitleTag.Text().Trim();
            subtitle = string.IsNullOrEmpty(tagText) ? null : tagText;
        }

        var authors = ParsePersons(mainElem.QuerySelector("li.bc-list-item.authorLabel"));

        if (authors is null)
        {
            throw new Exception("Could not parse authors");
        }

        var narrators = ParsePersons(mainElem.QuerySelector("li.bc-list-item.narratorLabel"));

        var durationText = ParseLength(mainElem);

        var genres = ParseGenres(mainElem);

        var series = ParseBookSeries(mainElem);

        string? imgUrl = null;
        var imgTag = mainElem.QuerySelector("img.bc-pub-block");
        if (imgTag is not null)
        {
            imgUrl = imgTag.Attributes["src"]?.Value;
        }

        string? description = null;
        string? copyright = null;
        string? publisher = null;
        int? year = null;
        var publisherSummaryTag = mainElem.QuerySelector("div.productPublisherSummary");
        if (publisherSummaryTag is not null)
        {
            var bcBoxesTags = publisherSummaryTag.QuerySelectorAll("div.bc-box");
            if (bcBoxesTags.Length >= 1)
            {
                description = bcBoxesTags[0].Text().Trim();
            }
            if (bcBoxesTags.Length > 1)
            {
                var copyrightText = bcBoxesTags.Last().Text().Trim();
                var yearMatches = _reYear.Matches(copyrightText);
                foreach (var yearMatch in yearMatches.ToList())
                {
                    var parsedYear = int.Parse(yearMatch.Value);
                    if (year is null || parsedYear < year)
                    {
                        year = parsedYear;
                    }
                }

                var copyrightPublisherMatch = _reCopyrightPublisher.Match(copyrightText);
                if (copyrightPublisherMatch.Success)
                {
                    copyright = copyrightPublisherMatch.Groups[1].Value;
                    publisher = copyrightPublisherMatch.Groups[2].Value;
                }
            }
        }

        var ratingResult = ParseRating(mainElem);

        string? asin = null;
        var asinInputTag = mainElem.QuerySelector("form#adbl-confirm-cash-purchase-form input[name='asin']");
        if (asinInputTag is not null)
        {
            asin = asinInputTag.Attributes["value"]?.Value;
        }
        else
        {
            asin = ParseAsinFromUrl(bookUrl);
        }

        return new BookSearchResult(bookUrl, title)
        {
            Authors = authors,
            Narrators = narrators ?? new List<Person>(),
            Subtitle = subtitle,
            Duration = durationText,
            Year = year,
            Language = null,
            ImageUrl = imgUrl,
            Series = series,
            Description = description,
            Genres = genres,
            Rating = ratingResult.Rating,
            NumberOfRatings = ratingResult.NumRatings,
            Copyright = copyright,
            Publisher = publisher,
            Asin = asin,
        };
    }

    private static (float? Rating, int? NumRatings) ParseRating(IElement mainElem)
    {
        (float? Rating, int? NumRatings) result = (rating: null, numRatings: null);
        var ratingTag = mainElem.QuerySelector("li.bc-list-item.ratingsLabel");
        if (ratingTag is not null)
        {
            var bcTextTags = ratingTag.QuerySelectorAll("span.bc-text");
            foreach (var bcTextTag in bcTextTags)
            {
                var bcTextTagText = bcTextTag.Text().Trim();
                var ratingMatch = _reRating.Match(bcTextTag.Text().Trim());
                if (ratingMatch.Success)
                {
                    result.Rating = float.Parse(ratingMatch.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                }
                var numRatingsMatch = _reNumRatings.Match(bcTextTagText);
                if (numRatingsMatch.Success)
                {
                    result.NumRatings = int.Parse(numRatingsMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                }
            }
        }

        return result;
    }

    private static IList<string> ParseGenres(IElement mainElem)
    {
        var genres = new HashSet<string>();
        var genresTag = mainElem.QuerySelector("li.bc-list-item.categoriesLabel");
        if (genresTag is not null)
        {
            genresTag
                .QuerySelectorAll("a")
                .Select(x => x.Text().Trim())
                .ToList()
                .ForEach(x => genres.Add(x));
        }
        var productTopicTags = mainElem.QuerySelector("div.product-topic-tags");
        if (productTopicTags is not null)
        {
            productTopicTags
                .QuerySelectorAll("span.bc-chip-wrapper a")
                .Select(x => x.Text().Trim())
                .ToList()
                .ForEach(x => genres.Add(x));
        }

        return genres.ToList();
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
        var match = _rePersonWithRole.Match(personString);
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
                if (!string.IsNullOrEmpty(nextSiblingText))
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
