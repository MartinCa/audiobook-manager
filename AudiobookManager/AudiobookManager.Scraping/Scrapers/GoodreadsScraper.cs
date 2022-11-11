using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Extensions;
using AudiobookManager.Scraping.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AudiobookManager.Scraping.Scrapers;
public class GoodreadsScraper : IScraper
{
    private const string _goodreadsDomain = "goodreads.com";
    private const string _goodreadsBaseUrl = $"https://www.{_goodreadsDomain}";
    private const string _sourceName = "Goodreads";

    private static List<string> _ignoredAuthorRoles = new() { "illustrator" };

    private static readonly Regex _reUrlWithoutQuery = new Regex(@"^([^\\?]+)", RegexOptions.Compiled);
    private static readonly Regex _reImgUrl = new(@"(.*_S\w)\d+(_\..*)", RegexOptions.Compiled);
    private static readonly Regex _reRating = new Regex(@"(\d\.?\d*) avg", RegexOptions.Compiled);
    private static readonly Regex _reNumRatings = new(@"([\d,]+) ratings", RegexOptions.Compiled);
    private static readonly Regex _reYear = new(@".*published\s+(\d+)", RegexOptions.Compiled);
    private static readonly Regex _reDetailsYear = new(@"\d{4}", RegexOptions.Compiled);
    private static readonly Regex _rePublisher = new(@"by(.+)", RegexOptions.Compiled);
    private static readonly Regex _reSeries = new(@"([^#]+)#?(.*)", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;

    public GoodreadsScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
    }

    public async Task<BookSearchResult> GetBookDetails(string bookUrl)
    {
        Dictionary<string, string> queryParameters = new()
        {
            ["utf8"] = "✓"
        };

        var uri = QueryHelpers.AddQueryString(bookUrl, queryParameters);
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting search results from Goodreads, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();

        HtmlParser parser = new();
        var doc = parser.ParseDocument(responseStream);

        var mainElement = doc.QuerySelector("div#topcol");

        if (mainElement is null)
        {
            throw new Exception($"Error getting search results from Goodreads, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        return await ParseBookDetails(doc, bookUrl);
    }

    public async Task<IList<BookSearchResult>> Search(string searchTerm)
    {
        Dictionary<string, string> queryParameters = new()
        {
            ["utf8"] = "✓",
            ["query"] = searchTerm
        };

        var uri = QueryHelpers.AddQueryString($"{_goodreadsBaseUrl}/search", queryParameters);
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error getting search results from Goodreads, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();

        HtmlParser parser = new();
        var doc = parser.ParseDocument(responseStream);

        var tableRowTags = doc.QuerySelectorAll("table.tableList tr");
        if (tableRowTags is not null && tableRowTags.Any())
        {
            return tableRowTags.Select(rowElem => ParseSearchResult(rowElem)).ToList();
        }

        var searchHeaderTag = doc.QuerySelector("h3.searchSubNavContainer");
        if (searchHeaderTag is not null && searchHeaderTag.Text().Trim().StartsWith("No results"))
        {
            return new List<BookSearchResult>();
        }

        throw new Exception("Invalid response from Goodreads");
    }

    public async Task<BookSearchResult> ParseBookDetails(IHtmlDocument doc, string bookUrl)
    {
        var mainElem = doc.QuerySelector("div#topcol");
        var allAuthors = ParseAuthors(mainElem);
        var authors = allAuthors.Where(x => x.Role is null || !_ignoredAuthorRoles.Contains(x.Role)).ToList();
        var narrators = allAuthors.Where(x => string.Equals(x.Role, "Narrator", StringComparison.InvariantCultureIgnoreCase)).ToList();

        string? bookName = null;
        string? subtitle = null;
        if (mainElem.TryGetTextFromQuerySelector("h1#bookTitle", out var rawTitleText))
        {
            var splitTitle = rawTitleText.Split(":");
            bookName = splitTitle[0];
            if (splitTitle.Length > 1)
            {
                subtitle = splitTitle[1].Trim();
            }
        }

        string? imgUrl = null;
        var imgTag = mainElem.QuerySelector("img#coverImage");
        if (imgTag is not null)
        {
            imgUrl = ConvertImgUrl(imgTag.Attributes["src"]?.Value);
        }

        var (year, publisher) = ParsePublisherTag(mainElem);

        var description = ParseDescription(mainElem);

        ParsedRating? ratingResult = null;
        var ratingTag = mainElem.QuerySelector("span[itemprop='ratingValue']");
        if (ratingTag is not null)
        {
            ratingResult = ParseRating(ratingTag.ParentElement.Text().Trim());
        }

        var bookSeries = await ParseBookSeries(mainElem);

        var genres = ParseGenres(doc);

        return new BookSearchResult(bookUrl, bookName)
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

    public static BookSearchResult ParseSearchResult(IElement resultElem)
    {
        var coverTag = resultElem.QuerySelector("td");
        var coverLinkTag = coverTag?.QuerySelector("a");
        var fullLink = $"{_goodreadsBaseUrl}{coverLinkTag?.Attributes["href"]?.Value}";
        string? link = null;
        var urlMatch = _reUrlWithoutQuery.Match(fullLink);
        if (urlMatch.Success)
        {
            link = urlMatch.Groups[1].Value;
        }

        if (link is null)
        {
            throw new Exception("Could not read link from Goodreads");
        }

        var bookName = coverLinkTag?.Attributes["title"]?.Value;

        if (bookName is null)
        {
            throw new Exception("Could not read book name from Goodreads");
        }

        var imgUrlSmall = coverTag?.QuerySelector("img")?.Attributes["src"]?.Value;
        var imgUrl = ConvertImgUrl(imgUrlSmall);

        var authors = ParseAuthors(resultElem);

        ParsedRating? rating = null;
        var ratingTag = resultElem.QuerySelector("span.minirating");
        if (resultElem.TryGetTextFromQuerySelector("span.minirating", out var ratingText))
        {
            rating = ParseRating(ratingText);
        }

        int? year = null;
        var publishedTag = ratingTag?.NextSibling;
        if (_reYear.TryMatch(publishedTag?.Text().Trim(), out var match))
        {
            year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return new BookSearchResult(link, bookName)
        {
            Authors = authors,
            Narrators = new List<Person>(),
            Subtitle = null,
            Duration = null,
            Year = year,
            Language = null,
            ImageUrl = imgUrl,
            Series = new List<BookSeriesSearchResult>(),
            Description = null,
            Genres = new List<string>(),
            Rating = rating?.Rating,
            NumberOfRatings = rating?.NumberOfRatings,
            Copyright = null,
            Publisher = null,
            Asin = null,
        };
    }

    public bool IsSource(string sourceName) => string.Equals(sourceName, _sourceName, StringComparison.InvariantCultureIgnoreCase);
    public bool SupportsUrl(string url) => url.Contains(_goodreadsDomain);

    private static IList<string> ParseGenres(IHtmlDocument doc)
    {
        var genres = new List<string>();
        var genreHeaderTag = doc.QuerySelectorAll("h2.brownBackground").ToList().Where(x => x.Text().Trim() == "Genres").FirstOrDefault();

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
                        if (genres.Count > 4)
                        {
                            break;
                        }
                    }
                }
            }

        }

        return genres;
    }

    private async Task<IList<BookSeriesSearchResult>> ParseBookSeries(IElement mainElem)
    {
        var series = new List<BookSeriesSearchResult>();

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
                        if (_reSeries.TryMatch(itemATag.Text().Trim(), out var seriesMatch))
                        {
                            series.Add(new BookSeriesSearchResult(seriesMatch.Groups[1].Value.Trim())
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
            var yearMatches = _reDetailsYear.Matches(publisherText);
            foreach (var yearMatch in yearMatches.ToList())
            {
                var parsedYear = int.Parse(yearMatch.Value);
                if (result.Year is null || parsedYear < result.Year)
                {
                    result.Year = parsedYear;
                }
            }

            if (_rePublisher.TryMatch(publisherText, out var publisherMatch))
            {
                result.Publisher = publisherMatch.Groups[1].Value.Trim();
            }
        }

        return result;
    }

    private static ParsedRating ParseRating(string ratingText)
    {
        ParsedRating rating = new();

        if (_reRating.TryMatch(ratingText, out var ratingMatch))
        {
            rating.Rating = float.Parse(ratingMatch.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        if (_reNumRatings.TryMatch(ratingText, out var numRatingsMatch))
        {
            rating.NumberOfRatings = int.Parse(numRatingsMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        return rating;
    }

    private static string? ConvertImgUrl(string? imgUrl)
    {
        if (imgUrl is null)
        {
            return null;
        }

        var imgUrlMatch = _reImgUrl.Match(imgUrl);
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

    private class PublisherParseResult
    {
        public int? Year { get; set; }
        public string? Publisher { get; set; }
    }
}
