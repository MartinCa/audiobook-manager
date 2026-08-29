using System.Net;
using System.Text.Json;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Scraping.Scrapers;

[TestClass]
public class HardcoverScraperTests
{
    /// <summary>
    /// Stands in for the "hardcover" named HttpClient. Records every outgoing request body
    /// (so tests can assert on the GraphQL query actually sent) and replays a single
    /// pre-canned JSON response.
    /// </summary>
    private class FakeHardcoverHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _status;

        public FakeHardcoverHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _status = status;
        }

        public List<string> CapturedRequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            CapturedRequestBodies.Add(body);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static HardcoverScraper CreateScraper(string responseJson, out FakeHardcoverHandler handler, string? apiKey = "test-api-key")
    {
        handler = new FakeHardcoverHandler(responseJson);
        var httpClient = new HttpClient(handler);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("hardcover")).Returns(httpClient);

        var bookSeriesMapper = new Mock<IBookSeriesMapper>();
        bookSeriesMapper
            .Setup(x => x.MapBookSeries(It.IsAny<IList<MetadataSeriesSearchResult>>()))
            .Returns<IList<MetadataSeriesSearchResult>>(x => Task.FromResult(x));

        var logger = new Mock<ILogger<HardcoverScraper>>();
        var settings = Options.Create(new AudiobookManagerSettings { HardcoverApiKey = apiKey });

        return new HardcoverScraper(httpClientFactory.Object, bookSeriesMapper.Object, logger.Object, settings);
    }

    // ---------- Search() ----------

    private const string _searchResponseJson = """
        {
          "data": {
            "search": {
              "results": {
                "hits": [
                  {
                    "document": {
                      "id": "123",
                      "slug": "the-hobbit",
                      "title": "The Hobbit",
                      "subtitle": "There and Back Again",
                      "author_names": ["J.R.R. Tolkien"],
                      "image": { "url": "https://covers.hardcover.app/hobbit.jpg" },
                      "release_date": "1937-09-21",
                      "rating": 4.5,
                      "ratings_count": 1000
                    }
                  },
                  {
                    "document": {
                      "id": "456",
                      "title": "No Slug Book",
                      "author_names": ["Some Author"],
                      "release_year": "2001"
                    }
                  }
                ]
              }
            }
          }
        }
        """;

    [TestMethod]
    public async Task Search_MapsGraphqlSearchResponseToResults()
    {
        var target = CreateScraper(_searchResponseJson, out var handler);

        var results = await target.Search("hobbit");

        Assert.AreEqual(2, results.Count);

        var hobbit = results.Single(r => r.BookName == "The Hobbit");
        Assert.AreEqual("https://hardcover.app/books/the-hobbit", hobbit.Url);
        Assert.AreEqual("There and Back Again", hobbit.Subtitle);
        Assert.AreEqual(1, hobbit.Authors.Count);
        Assert.AreEqual("J.R.R. Tolkien", hobbit.Authors.Single().Name);
        Assert.AreEqual("https://covers.hardcover.app/hobbit.jpg", hobbit.ImageUrl);
        Assert.AreEqual(1937, hobbit.Year);
        Assert.IsTrue(Math.Abs(4.5 - hobbit.Rating!.Value) < 0.001);
        Assert.AreEqual(1000, hobbit.NumberOfRatings);

        var noSlug = results.Single(r => r.BookName == "No Slug Book");
        // Falls back to the numeric id in the URL when no slug is present.
        Assert.AreEqual("https://hardcover.app/books/456", noSlug.Url);
        // release_year fallback used when release_date is absent.
        Assert.AreEqual(2001, noSlug.Year);

        Assert.AreEqual(1, handler.CapturedRequestBodies.Count);
    }

    [TestMethod]
    public async Task Search_NoHits_ReturnsEmptyList()
    {
        var emptyResponse = """{ "data": { "search": { "results": { "hits": [] } } } }""";
        var target = CreateScraper(emptyResponse, out _);

        var results = await target.Search("nonexistent");

        Assert.AreEqual(0, results.Count);
    }

    // ---------- GetBookDetails() ----------

    private const string _bookDetailsResponseJson = """
        {
          "data": {
            "books_by_pk": {
              "id": 789,
              "title": "The Hobbit: There and Back Again",
              "subtitle": null,
              "description": "Bilbo Baggins goes on <b>an adventure</b>.<br />It is great.",
              "slug": "the-hobbit",
              "release_date": "1937-09-21",
              "rating": 4.5,
              "ratings_count": 2000,
              "cached_image": { "url": "https://covers.hardcover.app/hobbit-full.jpg" },
              "cached_tags": {
                "Genre": [
                  { "tag": "Fiction" },
                  { "tag": "Fantasy" },
                  { "tag": "Adventure" }
                ]
              },
              "contributions": [
                { "contribution": null, "author": { "name": "J.R.R. Tolkien" } },
                { "contribution": "Narrator", "author": { "name": "Rob Inglis" } }
              ],
              "book_series": [
                { "position": 1, "series": { "name": "Middle-earth" } }
              ],
              "default_audio_edition": {
                "isbn_13": "9780007487350",
                "asin": "B002SGA6VG",
                "audio_seconds": 39600,
                "publisher": { "name": "HarperCollins" },
                "language": { "language": "English" }
              },
              "default_physical_edition": null
            }
          }
        }
        """;

    [TestMethod]
    public async Task GetBookDetails_MapsGraphqlResponseToDomainModel()
    {
        var target = CreateScraper(_bookDetailsResponseJson, out var handler);

        var result = await target.GetBookDetails("https://hardcover.app/books/789");

        Assert.AreEqual("The Hobbit", result.BookName);
        Assert.AreEqual("There and Back Again", result.Subtitle);
        Assert.AreEqual("https://hardcover.app/books/789", result.Url);
        Assert.AreEqual(1937, result.Year);

        Assert.AreEqual(1, result.Authors.Count);
        Assert.AreEqual("J.R.R. Tolkien", result.Authors.Single().Name);
        Assert.AreEqual(1, result.Narrators.Count);
        Assert.AreEqual("Rob Inglis", result.Narrators.Single().Name);

        // HTML sanitized: tags stripped, <br /> turned into newline.
        Assert.IsFalse(result.Description!.Contains("<"));
        Assert.IsTrue(result.Description.Contains("Bilbo Baggins goes on an adventure."));

        // "Fiction" is filtered out via _ignoredGenres.
        Assert.AreEqual(2, result.Genres.Count);
        Assert.IsTrue(result.Genres.Contains("Fantasy"));
        Assert.IsFalse(result.Genres.Contains("Fiction"));

        Assert.IsTrue(Math.Abs(4.5 - result.Rating!.Value) < 0.001);
        Assert.AreEqual(2000, result.NumberOfRatings);

        Assert.AreEqual(1, result.Series!.Count);
        Assert.AreEqual("Middle-earth", result.Series.Single().SeriesName);
        Assert.AreEqual("1", result.Series.Single().SeriesPart);

        Assert.AreEqual("11 hrs and 0 mins", result.Duration);
        Assert.AreEqual("English", result.Language);
        Assert.AreEqual("HarperCollins", result.Publisher);
        Assert.AreEqual("9780007487350", result.Isbn);
        Assert.AreEqual("B002SGA6VG", result.Asin);

        Assert.AreEqual(1, handler.CapturedRequestBodies.Count);
    }

    [TestMethod]
    public async Task GetBookDetails_NumericIdUrl_QueriesByIdNotSlug()
    {
        var target = CreateScraper(_bookDetailsResponseJson, out var handler);

        await target.GetBookDetails("789");

        var body = handler.CapturedRequestBodies.Single();
        Assert.IsTrue(body.Contains("books_by_pk"), "a bare numeric identifier should query books_by_pk(id: ...)");
        Assert.IsTrue(body.Contains("\"id\":789"));
    }

    [TestMethod]
    public async Task GetBookDetails_AudioEditionMissingLanguageAndAsin_FallsBackToPhysicalEdition()
    {
        var json = """
            {
              "data": {
                "books_by_pk": {
                  "id": 999,
                  "title": "Fallback Test Book",
                  "default_audio_edition": {
                    "audio_seconds": 3600,
                    "language": null,
                    "asin": null
                  },
                  "default_physical_edition": {
                    "isbn_13": "9781234567890",
                    "asin": "B0PHYSICALASIN",
                    "publisher": { "name": "Physical Publisher" },
                    "language": { "language": "French" }
                  }
                }
              }
            }
            """;
        var target = CreateScraper(json, out _);

        var result = await target.GetBookDetails("999");

        Assert.IsNotNull(result);
        Assert.AreEqual("French", result.Language);
        Assert.AreEqual("B0PHYSICALASIN", result.Asin);
        Assert.AreEqual("9781234567890", result.Isbn);
        Assert.AreEqual("Physical Publisher", result.Publisher);
    }

    /// <summary>
    /// GetBookBySlug() reads through "data.books[0]" (a books(where:...) query returns an
    /// array), unlike the by-id path which reads "data.books_by_pk" (a single object) -
    /// so the slug test needs its own response shape.
    /// </summary>
    private const string _bookDetailsBySlugResponseJson = """
        {
          "data": {
            "books": [
              {
                "id": 789,
                "title": "The Hobbit: There and Back Again",
                "subtitle": null,
                "description": "Bilbo Baggins goes on <b>an adventure</b>.<br />It is great.",
                "slug": "the-hobbit",
                "release_date": "1937-09-21",
                "rating": 4.5,
                "ratings_count": 2000,
                "cached_image": { "url": "https://covers.hardcover.app/hobbit-full.jpg" },
                "cached_tags": {
                  "Genre": [
                    { "tag": "Fiction" },
                    { "tag": "Fantasy" },
                    { "tag": "Adventure" }
                  ]
                },
                "contributions": [
                  { "contribution": null, "author": { "name": "J.R.R. Tolkien" } },
                  { "contribution": "Narrator", "author": { "name": "Rob Inglis" } }
                ],
                "book_series": [
                  { "position": 1, "series": { "name": "Middle-earth" } }
                ],
                "default_audio_edition": {
                  "isbn_13": "9780007487350",
                  "asin": "B002SGA6VG",
                  "audio_seconds": 39600,
                  "publisher": { "name": "HarperCollins" },
                  "language": { "language": "English" }
                },
                "default_physical_edition": null
              }
            ]
          }
        }
        """;

    [TestMethod]
    public async Task GetBookDetails_SlugUrl_QueriesBySlugEquality()
    {
        var target = CreateScraper(_bookDetailsBySlugResponseJson, out var handler);

        await target.GetBookDetails("https://hardcover.app/books/the-hobbit");

        var body = handler.CapturedRequestBodies.Single();
        Assert.IsTrue(body.Contains("books(where:"), "a slug URL should query books(where: {slug: {_eq: ...}})");
        Assert.IsTrue(body.Contains("the-hobbit"));
    }

    [TestMethod]
    public async Task GetBookDetails_BookNotFound_Throws()
    {
        var nullResponse = """{ "data": { "books_by_pk": null } }""";
        var target = CreateScraper(nullResponse, out _);

        await Assert.ThrowsExactlyAsync<Exception>(() => target.GetBookDetails("999999"));
    }

    [TestMethod]
    public async Task ExecuteGraphqlQuery_GraphqlErrorInResponse_Throws()
    {
        var errorResponse = """
            { "errors": [ { "message": "field not found" } ] }
            """;
        var target = CreateScraper(errorResponse, out _);

        var ex = await Assert.ThrowsExactlyAsync<Exception>(() => target.Search("anything"));
        Assert.IsTrue(ex.Message.Contains("field not found"));
    }

    [TestMethod]
    public async Task ExecuteGraphqlQuery_NonSuccessStatusCode_Throws()
    {
        var handler = new FakeHardcoverHandler("Internal error", HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("hardcover")).Returns(httpClient);
        var bookSeriesMapper = new Mock<IBookSeriesMapper>();
        var logger = new Mock<ILogger<HardcoverScraper>>();
        var settings = Options.Create(new AudiobookManagerSettings { HardcoverApiKey = "key" });
        var target = new HardcoverScraper(httpClientFactory.Object, bookSeriesMapper.Object, logger.Object, settings);

        await Assert.ThrowsExactlyAsync<Exception>(() => target.Search("anything"));
    }

    // ---------- RequiresApiKey / IsApiKeyConfigured ----------

    [TestMethod]
    public void RequiresApiKey_IsAlwaysTrue()
    {
        var target = CreateScraper("{}", out _, apiKey: null);
        Assert.IsTrue(target.RequiresApiKey);
    }

    [TestMethod]
    public void IsApiKeyConfigured_FalseWhenSettingIsNull()
    {
        var target = CreateScraper("{}", out _, apiKey: null);
        Assert.IsFalse(target.IsApiKeyConfigured);
    }

    [TestMethod]
    public void IsApiKeyConfigured_FalseWhenSettingIsEmptyString()
    {
        var target = CreateScraper("{}", out _, apiKey: "");
        Assert.IsFalse(target.IsApiKeyConfigured);
    }

    [TestMethod]
    public void IsApiKeyConfigured_TrueWhenSettingIsPresent()
    {
        var target = CreateScraper("{}", out _, apiKey: "some-real-key");
        Assert.IsTrue(target.IsApiKeyConfigured);
    }

    [TestMethod]
    public void SourceName_And_IsSource()
    {
        var target = CreateScraper("{}", out _);
        Assert.AreEqual("Hardcover", target.SourceName);
        Assert.IsTrue(target.IsSource("hardcover"));
        Assert.IsTrue(target.IsSource("HARDCOVER"));
        Assert.IsFalse(target.IsSource("Goodreads"));
    }

    [TestMethod]
    public void SupportsUrl_MatchesHardcoverDomainOnly()
    {
        var target = CreateScraper("{}", out _);
        Assert.IsTrue(target.SupportsUrl("https://hardcover.app/books/the-hobbit"));
        Assert.IsFalse(target.SupportsUrl("https://goodreads.com/book/show/1"));
    }

    // ---------- Disabled Hasura filter operators (CLAUDE.md limitation) ----------

    /// <summary>
    /// Hardcover disables pattern-matching filter operators server-side (see CLAUDE.md /
    /// docs.hardcover.app "Limitations": _like, _nlike, _ilike, _niregex, _nregex, _iregex,
    /// _regex, _nsimilar, _similar all return HTTP 403). None of the scraper's outgoing
    /// GraphQL queries may ever reference one of these operators.
    /// </summary>
    private static readonly string[] _disabledOperators =
    {
        "_ilike", "_like", "_nlike", "_niregex", "_nregex", "_iregex", "_regex", "_nsimilar", "_similar",
    };

    [TestMethod]
    public async Task Search_NeverSendsADisabledPatternMatchingOperator()
    {
        var target = CreateScraper(_searchResponseJson, out var handler);
        await target.Search("some term with % and _ characters");

        AssertNoDisabledOperators(handler.CapturedRequestBodies.Single());
    }

    [TestMethod]
    public async Task SearchSeries_NeverSendsADisabledPatternMatchingOperator()
    {
        var seriesSearchResponse = """
            { "data": { "search": { "results": { "hits": [] } } } }
            """;
        var target = CreateScraper(seriesSearchResponse, out var handler);
        await target.SearchSeries("some series");

        AssertNoDisabledOperators(handler.CapturedRequestBodies.Single());
    }

    [TestMethod]
    public async Task GetBookDetails_NeverSendsADisabledPatternMatchingOperator()
    {
        var target = CreateScraper(_bookDetailsBySlugResponseJson, out var handler);
        await target.GetBookDetails("https://hardcover.app/books/the-hobbit");

        AssertNoDisabledOperators(handler.CapturedRequestBodies.Single());
    }

    [TestMethod]
    public async Task GetSeriesBooks_NeverSendsADisabledPatternMatchingOperator()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Middle-earth",
                  "slug": "middle-earth",
                  "book_series": []
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);
        await target.GetSeriesBooks("1");

        AssertNoDisabledOperators(handler.CapturedRequestBodies.Single());

        // Also check the slug-based series query path.
        var target2 = CreateScraper("""{ "data": { "series": [] } }""", out var handler2);
        await target2.GetSeriesBooks("https://hardcover.app/series/middle-earth");
        AssertNoDisabledOperators(handler2.CapturedRequestBodies.Single());
    }

    private static void AssertNoDisabledOperators(string requestBody)
    {
        // Parse out just the "query" field so we inspect the actual GraphQL query text sent
        // over the wire (not variable values that might coincidentally contain the substring).
        var parsed = JsonDocument.Parse(requestBody);
        var query = parsed.RootElement.GetProperty("query").GetString() ?? "";

        foreach (var op in _disabledOperators)
        {
            Assert.IsFalse(
                query.Contains(op, StringComparison.OrdinalIgnoreCase),
                $"query must never use the disabled Hasura operator '{op}' (see CLAUDE.md Hardcover limitations): {query}");
        }
    }
}
