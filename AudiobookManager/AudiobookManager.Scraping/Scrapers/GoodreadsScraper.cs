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

namespace AudiobookManager.Scraping.Scrapers;
public partial class GoodreadsScraper : IScraper
{
    private const string _goodreadsDomain = "goodreads.com";
    private const string _goodreadsBaseUrl = $"https://www.{_goodreadsDomain}";
    private const string _sourceName = "Goodreads";
    private const int _maxNumGenresToGet = 5;

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
                _logger.LogInformation("{BookUrl} is in old format", bookUrl);
                return await ParseLegacyBookDetails(doc, bookUrl);
            }

            var scriptElementNew = doc.QuerySelector("script[id=__NEXT_DATA__]");
            if (scriptElementNew is not null)
            {
                _logger.LogInformation("{BookUrl} is in new 2022 format", bookUrl);
                return await ParseNewBookJson(scriptElementNew.Text(), bookUrl);
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

    public async Task<BookSearchResult> ParseNewBookJson(string newBookJson, string bookUrl)
    {
        (var bookElement, var workElement, var contributorElements, var seriesElements) = GetBookJsonElements(newBookJson);

        var allPersons = ParseJsonPersons(bookElement, contributorElements);
        var narrators = allPersons.Where(x => string.Equals(x.Role, "Narrator", StringComparison.InvariantCultureIgnoreCase)).ToList();
        var authors = FilterAuthors(allPersons.Except(narrators));

        string? bookName = null;
        string? subtitle = null;
        var bookTitle = bookElement.GetPropertyValueOrNull("title");
        if (bookTitle is not null)
        {
            var splitTitle = bookTitle.Split(":");
            bookName = splitTitle[0].Trim();
            if (splitTitle.Length > 1)
            {
                subtitle = splitTitle[1].Trim();
            }
        }

        string? imgUrl = bookElement.GetPropertyValueOrNull("imageUrl");

        var publicationTimeElement = workElement.GetNestedProperty("details", "publicationTime");
        var publishUnixMsTimestamp = publicationTimeElement.GetInt64();
        var publicationDateTime = DateTime.UnixEpoch.AddMilliseconds(publishUnixMsTimestamp);
        var year = publicationDateTime.Year;

        var description = bookElement.GetPropertyValueOrNull("description");

        var genres = ParseGenres(bookElement);

        var ratingResult = ParseRating(workElement);

        var series = await ParseSeries(bookElement, seriesElements);

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

    private static (JsonElement BookElement, JsonElement WorkElement, List<JsonElement> ContributorElements, List<JsonElement> SeriesElements) GetBookJsonElements(string jsonText) {

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

    private async Task<IList<BookSeriesSearchResult>> ParseSeries(JsonElement bookElement, IList<JsonElement> seriesElements)
    {
        var seriesPositions = bookElement
            .GetProperty("bookSeries")
            .EnumerateArray()
            .Select(e => (Position: e.GetPropertyValueOrNull("userPosition"), Ref: e.GetNestedProperty("series", "__ref").GetString()?.Substring(7)))
            .Where(x => x.Ref is not null)
            .Cast<(string? Position, string Ref)>();

        var seriesTitles = seriesElements
            .Select(x => ( Id: x.GetPropertyValueOrNull("id"), Title: x.GetPropertyValueOrNull("title") ))
            .Where(x => x.Id is not null && x.Title is not null)
            .Cast<(string Id, string Title)>();

        var seriesMap = seriesTitles.ToDictionary(x => x.Id, x => x.Title);

        var series = seriesPositions.Select(x =>
        {
            if (seriesMap.TryGetValue(x.Ref, out var result))
            {
                return new BookSeriesSearchResult(result)
                {
                    SeriesPart = x.Position
                };
            }

            return null;
        })
            .Where(x => x is not null)
            .Cast<BookSeriesSearchResult>()
            .ToList();

        return await _bookSeriesMapper.MapBookSeries(series);
    }

    private async Task<BookSearchResult> ParseLegacyBookDetails(IHtmlDocument doc, string bookUrl)
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

        var contributorRoleMap = contributorEdges.Select(x => new { Ref = x.GetNestedProperty("node", "__ref").GetString().Substring(12), Role = x.GetProperty("role").GetString() });

        var contributorNameMap = contributorElements.ToDictionary(x => x.GetProperty("id").GetString(), x => x.GetPropertyValueOrNull("name"));

        return contributorRoleMap.Select(x =>
        {
            if (contributorNameMap.TryGetValue(x.Ref, out var result))
            {
                return new Person(result)
                {
                    Role = x.Role
                };
            }

            return null;
        })
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
