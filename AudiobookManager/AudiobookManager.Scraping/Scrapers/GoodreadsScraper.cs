using System.Globalization;
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

namespace AudiobookManager.Scraping.Scrapers;
public partial class GoodreadsScraper : IScraper
{
    private const string _goodreadsDomain = "goodreads.com";
    private const string _goodreadsBaseUrl = $"https://www.{_goodreadsDomain}";
    private const string _sourceName = "Goodreads";

    private static readonly IList<string> _ignoredAuthorRoles = new List<string> { "illustrator" };
    private static readonly IList<string> _ignoredGenres = new List<string> { "Fiction" };

    [GeneratedRegex(@"^([^\\?]+)")]
    private static partial Regex ReUrlWithoutQuery();
    [GeneratedRegex(@"(.*_S\w)\d+(_\..*)")]
    private static partial Regex ReImgUrl();
    [GeneratedRegex(@"(\d\.?\d*) avg")]
    private static partial Regex ReRating();
    [GeneratedRegex(@"([\d,]+)\s?ratings")]
    private static partial Regex ReNumRatings();
    [GeneratedRegex(@".*published\s+(\d+)")]
    private static partial Regex ReYear();
    [GeneratedRegex(@"\d{4}")]
    private static partial Regex ReDetailsYear();
    [GeneratedRegex(@"by(.+)")]
    private static partial Regex RePublisher();
    [GeneratedRegex(@"([^#]+)#?(.*)")]
    private static partial Regex ReSeries();
    [GeneratedRegex(@"\(#([^\)]+)\)")]
    private static partial Regex ReNewDetailsSeriesPart();

    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;
    private readonly ILogger<GoodreadsScraper> _logger;

    public GoodreadsScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper, ILogger<GoodreadsScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
        _logger = logger;

        int maxRetries = 5;

        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(
            retryCount: maxRetries,
            sleepDurationProvider: (int retryNum) => TimeSpan.FromSeconds(3),
            onRetry: (exception, sleepDuration, attemptNumber, context) =>
            {
                _logger.LogWarning("Exception: {exceptionMessage}, Retrying in {sleepDuration}. {attemptNumber}/{maxRetries}", exception.Message, sleepDuration, attemptNumber, maxRetries);
            });
    }

    public async Task<BookSearchResult> GetBookDetails(string bookUrl)
    {
        Dictionary<string, string> queryParameters = new()
        {
            ["utf8"] = "✓"
        };

        var uri = QueryHelpers.AddQueryString(bookUrl, queryParameters);
        var httpClient = _httpClientFactory.CreateClient();

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await httpClient.GetAsync(uri);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error getting search results from Goodreads, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
            }

            var responseStream = await response.Content.ReadAsStreamAsync();

            HtmlParser parser = new();
            var doc = parser.ParseDocument(responseStream);

            var mainElementOld = doc.QuerySelector("div#topcol");
            if (mainElementOld is not null)
            {
                return await ParseBookDetails(doc, bookUrl);
            }

            var mainElemenNew = doc.QuerySelector("main.BookPage");
            if (mainElemenNew is not null)
            {
                return await ParseNewBookDetails(mainElemenNew, bookUrl);
            }

            throw new Exception($"Error getting search results from Goodreads, status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
        });
    }

    public async Task<IList<BookSearchResult>> Search(string searchTerm)
    {
        var termTokens = searchTerm.Split(" ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Dictionary<string, string> queryParameters = new()
        {
            ["utf8"] = "✓",
            ["search_type"] = "books"
        };

        var uri = QueryHelpers.AddQueryString($"{_goodreadsBaseUrl}/search", queryParameters);
        uri += $"&search[query]={string.Join("+", termTokens)}";
        var httpClient = _httpClientFactory.CreateClient();

        return await _retryPolicy.ExecuteAsync(async () =>
        {
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
        });
    }



    public bool IsSource(string sourceName) => string.Equals(sourceName, _sourceName, StringComparison.InvariantCultureIgnoreCase);

    public bool SupportsUrl(string url) => url.Contains(_goodreadsDomain);

    public string SourceName => _sourceName;

    private async Task<BookSearchResult> ParseNewBookDetails(IElement mainElem, string bookUrl)
    {
        var allPersons = ParseNewDetailsPersons(mainElem);
        var narrators = allPersons.Where(x => string.Equals(x.Role, "Narrator", StringComparison.InvariantCultureIgnoreCase)).ToList();
        var authors = FilterAuthors(allPersons.Except(narrators));

        string? bookName = null;
        string? subtitle = null;
        if (mainElem.TryGetTextFromQuerySelector("div.BookPageTitleSection h1", out var rawBookTitle))
        {
            var splitTitle = rawBookTitle.Split(":");
            bookName = splitTitle[0].Trim();
            if (splitTitle.Length > 1)
            {
                subtitle = splitTitle[1].Trim();
            }
        }

        string? imgUrl = null;
        var imgTag = mainElem.QuerySelector("div.BookCover img");
        if (imgTag is not null)
        {
            imgUrl = imgTag.Attributes["src"]?.Value;
        }

        int? year = null;
        var publicationDetailsTag = mainElem.QuerySelector("p[data-testid='publicationInfo']");
        if (publicationDetailsTag is not null && ReDetailsYear().TryMatch(publicationDetailsTag.Text(), out var match))
        {
            year = int.Parse(match.Value);
        }

        string? description = null;
        if (mainElem.TryGetTextFromQuerySelector("div.BookPageMetadataSection__description", out var descriptionText))
        {
            description = descriptionText;
        }

        var genres = ParseNewDetailsGenres(mainElem);

        ParsedRating? ratingResult = ParseNewDetailsRating(mainElem);

        var series = await ParseNewDetailsSeries(mainElem);

        return new BookSearchResult(bookUrl, bookName)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = null,
            Year = year,
            Language = null,
            ImageUrl = imgUrl,
            Series = series,
            Description = description,
            Genres = genres,
            Rating = ratingResult?.Rating,
            NumberOfRatings = ratingResult?.NumberOfRatings,
            Copyright = null,
            Publisher = null,
            Asin = null,
        };
    }

    private async Task<BookSearchResult> ParseBookDetails(IHtmlDocument doc, string bookUrl)
    {
        var mainElem = doc.QuerySelector("div#topcol");
        var allAuthors = ParseAuthors(mainElem);
        var narrators = allAuthors.Where(x => string.Equals(x.Role, "Narrator", StringComparison.InvariantCultureIgnoreCase)).ToList();
        var authors = FilterAuthors(allAuthors.Except(narrators));

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
            ratingResult = ParseOldDetailsRating(ratingTag);
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

    private static BookSearchResult ParseSearchResult(IElement resultElem)
    {
        var coverTag = resultElem.QuerySelector("td");
        var coverLinkTag = coverTag?.QuerySelector("a");
        var fullLink = $"{_goodreadsBaseUrl}{coverLinkTag?.Attributes["href"]?.Value}";
        string? link = null;
        var urlMatch = ReUrlWithoutQuery().Match(fullLink);
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
        if (ReYear().TryMatch(publishedTag?.Text().Trim(), out var match))
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

    private async Task<IList<BookSeriesSearchResult>> ParseNewDetailsSeries(IElement mainElem)
    {
        var resultingSeries = new List<BookSeriesSearchResult>();

        foreach (var workDetailsTag in mainElem.QuerySelectorAll("div.WorkDetails div.DescListItem").ToList())
        {
            var dtTagText = workDetailsTag.QuerySelector("dt")?.Text().Trim();
            if (dtTagText is null || dtTagText != "Series")
            {
                continue;
            }

            foreach (var aTag in workDetailsTag.QuerySelectorAll("a").ToList())
            {
                var series = new BookSeriesSearchResult(aTag.Text().Trim());
                var seriesPart = aTag.NextSibling?.Text().Trim();
                if (seriesPart is not null && ReNewDetailsSeriesPart().TryMatch(seriesPart, out var seriesPartMatch))
                {
                    series.SeriesPart = seriesPartMatch.Groups[1].Value;
                }
                resultingSeries.Add(series);
            }
        }

        return await _bookSeriesMapper.MapBookSeries(resultingSeries);
    }

    private static ParsedRating? ParseNewDetailsRating(IElement mainElem)
    {
        var parsedResult = new ParsedRating();
        if (mainElem.TryGetTextFromQuerySelector("div.RatingStatistics__rating", out var ratingText))
        {
            parsedResult.Rating = float.Parse(ratingText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        if (mainElem.TryGetTextFromQuerySelector("span[data-testid='ratingsCount']", out var ratingsCountText) && ReNumRatings().TryMatch(ratingsCountText, out var ratingsCountMatch))
        {
            parsedResult.NumberOfRatings = int.Parse(ratingsCountMatch.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        return parsedResult;
    }

    private static IList<Person> FilterAuthors(IEnumerable<Person> persons)
    {
        return persons
            .Where(p => p.Role is null || !_ignoredAuthorRoles.Any(ignoredRole => string.Equals(ignoredRole, p.Role, StringComparison.InvariantCultureIgnoreCase)))
            .ToList();
    }

    private static IList<Person> ParseNewDetailsPersons(IElement mainElem)
    {
        var persons = new List<Person>();

        var contributorLinks = mainElem.QuerySelectorAll("div.ContributorLinksList a.ContributorLink");

        foreach (var contributorLink in contributorLinks.ToList())
        {
            if (contributorLink.TryGetTextFromQuerySelector("span.ContributorLink__name", out var personName))
            {
                var person = new Person(personName);

                if (contributorLink.TryGetTextFromQuerySelector("span.ContributorLink__role", out var roleName))
                {
                    person.Role = roleName;
                }

                persons.Add(person);
            }
        }

        return persons;
    }

    private static IList<string> FilterGenres(IEnumerable<string> genres)
    {
        return genres
            .Where(genreText => _ignoredGenres.All(ignoredGenre => !string.Equals(ignoredGenre, genreText, StringComparison.InvariantCultureIgnoreCase)))
            .Take(5)
            .ToList();
    }

    private static IList<string> ParseNewDetailsGenres(IElement mainElem)
    {
        var genreTags = mainElem.QuerySelectorAll("div.BookPageMetadataSection__genres span.BookPageMetadataSection__genreButton");
        return FilterGenres(genreTags
            .ToList()
            .Select(genreElement => genreElement.Text().Trim()));
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
                        if (ReSeries().TryMatch(itemATag.Text().Trim(), out var seriesMatch))
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

    private static ParsedRating ParseRating(string ratingText)
    {
        ParsedRating rating = new();

        if (ReRating().TryMatch(ratingText, out var ratingMatch))
        {
            rating.Rating = float.Parse(ratingMatch.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        if (ReNumRatings().TryMatch(ratingText, out var numRatingsMatch))
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

    private class PublisherParseResult
    {
        public int? Year { get; set; }
        public string? Publisher { get; set; }
    }
}
