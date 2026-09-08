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

    // ---------- Search() series parsing ----------

    // Shapes confirmed live against the API (2026-09): featured_series is an object with a
    // nested series.name and a float position; absent is null or {} (box-set/compilation hits);
    // series_names is a flat name list with no positions.
    private const string _searchResponseWithSeriesJson = """
        {
          "data": {
            "search": {
              "results": {
                "hits": [
                  {
                    "document": {
                      "id": "123",
                      "slug": "ashes-of-man",
                      "title": "Ashes of Man",
                      "author_names": ["Christopher Ruocchio"],
                      "series_names": ["The Sun Eater"],
                      "featured_series": {
                        "collection": false,
                        "featured": true,
                        "id": 100075,
                        "position": 5.0,
                        "series": {
                          "id": 6522,
                          "name": "The Sun Eater",
                          "slug": "the-sun-eater",
                          "books_count": 18,
                          "primary_books_count": 7
                        }
                      },
                      "featured_series_position": 5.0
                    }
                  },
                  {
                    "document": {
                      "id": "456",
                      "title": "Sun Eater Series 5 Books Set",
                      "author_names": ["Christopher Ruocchio"],
                      "series_names": [],
                      "featured_series": {},
                      "featured_series_position": null
                    }
                  }
                ]
              }
            }
          }
        }
        """;

    [TestMethod]
    public async Task Search_ExtractsSeriesFromFeaturedSeriesInSearchDocument()
    {
        var target = CreateScraper(_searchResponseWithSeriesJson, out _);

        var results = await target.Search("ashes of man");

        var withSeries = results.Single(r => r.BookName == "Ashes of Man");
        Assert.AreEqual(1, withSeries.Series!.Count);
        Assert.AreEqual("The Sun Eater", withSeries.Series.Single().SeriesName);
        Assert.AreEqual("5", withSeries.Series.Single().SeriesPart);
    }

    [TestMethod]
    public async Task Search_EmptyFeaturedSeriesObject_YieldsNoSeries()
    {
        var target = CreateScraper(_searchResponseWithSeriesJson, out _);

        var results = await target.Search("sun eater set");

        // The box-set hit: featured_series is {} and series_names is [] - the
        // "present but empty" shape the API returns instead of null.
        var boxSet = results.Single(r => r.BookName == "Sun Eater Series 5 Books Set");
        Assert.AreEqual(0, boxSet.Series!.Count);
    }

    [TestMethod]
    public async Task Search_SeriesNamesWithoutFeaturedSeries_YieldsNameOnlyEntries()
    {
        var responseJson = """
            {
              "data": {
                "search": {
                  "results": {
                    "hits": [
                      {
                        "document": {
                          "id": "789",
                          "title": "Multi Series Book",
                          "series_names": ["The Sun Eater", "Empire of Silence"]
                        }
                      }
                    ]
                  }
                }
              }
            }
            """;
        var target = CreateScraper(responseJson, out _);

        var results = await target.Search("multi series book");

        var series = results.Single().Series!;
        Assert.AreEqual(2, series.Count);
        Assert.AreEqual("The Sun Eater", series[0].SeriesName);
        Assert.IsNull(series[0].SeriesPart);
        Assert.AreEqual("Empire of Silence", series[1].SeriesName);
        Assert.IsNull(series[1].SeriesPart);
    }

    [TestMethod]
    public async Task Search_SeriesNames_DeduplicatesAgainstFeaturedSeries()
    {
        var responseJson = """
            {
              "data": {
                "search": {
                  "results": {
                    "hits": [
                      {
                        "document": {
                          "id": "789",
                          "title": "Dedup Book",
                          "series_names": ["the sun eater", "Other Series"],
                          "featured_series": {
                            "position": 2,
                            "series": { "id": 1, "name": "The Sun Eater" }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """;
        var target = CreateScraper(responseJson, out _);

        var results = await target.Search("dedup");

        var series = results.Single().Series!;
        Assert.AreEqual(2, series.Count);
        // The featured entry wins - it carries the position; the flat name is dropped.
        Assert.AreEqual("The Sun Eater", series[0].SeriesName);
        Assert.AreEqual("2", series[0].SeriesPart);
        Assert.AreEqual("Other Series", series[1].SeriesName);
    }

    [TestMethod]
    public async Task Search_StringEncodedFeaturedSeries_IsParsed()
    {
        var responseJson = """
            {
              "data": {
                "search": {
                  "results": {
                    "hits": [
                      {
                        "document": {
                          "id": "789",
                          "title": "Encoded Series Book",
                          "featured_series": "{\"position\":5.0,\"series\":{\"id\":6522,\"name\":\"The Sun Eater\"}}"
                        }
                      }
                    ]
                  }
                }
              }
            }
            """;
        var target = CreateScraper(responseJson, out _);

        var results = await target.Search("encoded");

        var series = results.Single().Series!;
        Assert.AreEqual(1, series.Count);
        Assert.AreEqual("The Sun Eater", series.Single().SeriesName);
        Assert.AreEqual("5", series.Single().SeriesPart);
    }

    [TestMethod]
    public async Task Search_FractionalFeaturedSeriesPosition_KeepsDecimal()
    {
        var responseJson = """
            {
              "data": {
                "search": {
                  "results": {
                    "hits": [
                      {
                        "document": {
                          "id": "789",
                          "title": "Novella Book",
                          "featured_series": {
                            "position": 5.5,
                            "series": { "id": 1, "name": "The Sun Eater" }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """;
        var target = CreateScraper(responseJson, out _);

        var results = await target.Search("novella");

        Assert.AreEqual("5.5", results.Single().Series!.Single().SeriesPart);
    }

    [TestMethod]
    public async Task Search_MapsAllSeriesThroughBookSeriesMapperInOneCall()
    {
        // Own scraper + mapper mock so the mapping call can be asserted on directly.
        var handler = new FakeHardcoverHandler(_searchResponseWithSeriesJson);
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("hardcover")).Returns(httpClient);

        var mapper = new Mock<IBookSeriesMapper>();
        mapper
            .Setup(x => x.MapBookSeriesPerBook(It.IsAny<IList<IList<MetadataSeriesSearchResult>>>()))
            .ReturnsAsync((IList<IList<MetadataSeriesSearchResult>> books) => books);

        var logger = new Mock<ILogger<HardcoverScraper>>();
        var settings = Options.Create(new AudiobookManagerSettings { HardcoverApiKey = "test-api-key" });

        var target = new HardcoverScraper(httpClientFactory.Object, mapper.Object, logger.Object, settings);

        var results = await target.Search("sun eater");

        // One call for the whole result set (not per hit), shaped per book: group 0 is the
        // "Ashes of Man" hit's series, group 1 the box-set hit's (empty).
        mapper.Verify(x => x.MapBookSeriesPerBook(It.IsAny<IList<IList<MetadataSeriesSearchResult>>>()), Times.Once);
        var passedGroups = mapper.Invocations[0].Arguments[0] as IList<IList<MetadataSeriesSearchResult>>;
        Assert.IsNotNull(passedGroups);
        Assert.AreEqual(2, passedGroups.Count);
        Assert.AreEqual(1, passedGroups[0].Count);
        Assert.AreEqual("The Sun Eater", passedGroups[0].Single().SeriesName);
        Assert.AreEqual(0, passedGroups[1].Count);

        // And the mapped groups are written back onto the matching results.
        Assert.AreEqual("The Sun Eater", results.Single(r => r.BookName == "Ashes of Man").Series!.Single().SeriesName);
        Assert.AreEqual(0, results.Single(r => r.BookName == "Sun Eater Series 5 Books Set").Series!.Count);
    }

    [TestMethod]
    public async Task Search_NoSeriesFields_SeriesStaysEmpty()
    {
        var target = CreateScraper(_searchResponseJson, out _);

        var results = await target.Search("hobbit");

        // Both fixture hits carry no series fields at all.
        Assert.IsTrue(results.All(r => r.Series is null || r.Series.Count == 0));
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

    [TestMethod]
    public void SupportsUrl_UrlMerelyMentioningTheDomain_IsRejected()
    {
        // The URL handed to GetBookDetails comes straight from the caller and the parsed page is
        // returned in the response, so a substring match here was a request-forgery primitive:
        // every one of these passed the old url.Contains("hardcover.app") check.
        var target = CreateScraper("{}", out _);

        Assert.IsFalse(target.SupportsUrl("http://169.254.169.254/latest/meta-data?ref=hardcover.app"));
        Assert.IsFalse(target.SupportsUrl("https://hardcover.app.example.net/books/x"));
        Assert.IsFalse(target.SupportsUrl("https://example.net/?to=https://hardcover.app/books/x"));
        Assert.IsFalse(target.SupportsUrl("file:///etc/passwd"));
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

    // ---------- GetSeriesBooks() per-position dedupe ----------

    // Translated editions are recorded as their own independent book rows at the same position
    // (canonical_id null), so the roster must be deduped to the most popular book per position.

    [TestMethod]
    public async Task GetSeriesBooks_DeduplicatesTranslatedEditionsAtTheSamePosition()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Jack Reacher",
                  "slug": "jack-reacher",
                  "book_series": [
                    { "position": 1, "compilation": false, "book": { "id": 1, "title": "Killing Floor", "slug": "killing-floor", "release_date": "2001-01-01", "compilation": false, "users_count": 50000 } },
                    { "position": 2, "compilation": false, "book": { "id": 2, "title": "Die Trying", "slug": "die-trying", "release_date": "2002-01-01", "compilation": false, "users_count": 40000 } },
                    { "position": 2, "compilation": false, "book": { "id": 3, "title": "Les caves de la Maison Blanche", "slug": "les-caves", "release_date": "2002-01-01", "compilation": false, "users_count": 200 } },
                    { "position": 2, "compilation": false, "book": { "id": 4, "title": "ThaiTitle", "slug": "thai", "release_date": "2002-01-01", "compilation": false, "users_count": 50 } }
                  ]
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);

        var result = await target.GetSeriesBooks("1");

        // Position 2 carries the English, French and Thai titles - only the most popular
        // (Die Trying) is kept, alongside the sole position 1 entry.
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(
            new[] { "Killing Floor", "Die Trying" },
            result.Books.Select(b => b.Title).ToList());
        Assert.AreEqual(2, result.Books.Count);
        // BookCount reflects the deduped roster (mirrors how AudibleScraper counts).
        Assert.AreEqual(2, result.BookCount);
    }

    [TestMethod]
    public async Task GetSeriesBooks_KeepsIndividualAndOmnibusButDedupesOmnibusAtTheSamePosition()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Reacher",
                  "slug": "reacher",
                  "book_series": [
                    { "position": 1, "compilation": false, "book": { "id": 10, "title": "Killing Floor", "slug": "kf", "release_date": null, "compilation": false, "users_count": 100 } },
                    { "position": 1, "compilation": true, "book": { "id": 20, "title": "Reacher 1-3 Box Set", "slug": "box", "release_date": null, "compilation": false, "users_count": 50 } },
                    { "position": 2, "compilation": true, "book": { "id": 30, "title": "Reacher 4-6 Box Set", "slug": "box46", "release_date": null, "compilation": false, "users_count": 300 } },
                    { "position": 2, "compilation": true, "book": { "id": 31, "title": "Reacher 4-6 Box Set Deluxe", "slug": "box46d", "release_date": null, "compilation": false, "users_count": 900 } }
                  ]
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);

        var result = await target.GetSeriesBooks("1");

        // Position 1 keeps the individual book AND the omnibus (different partitions, so the
        // per-series "Include omnibus editions" setting can still show both). Position 2 has two
        // compilations - only the most popular one is kept.
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(
            new[] { "Killing Floor", "Reacher 1-3 Box Set", "Reacher 4-6 Box Set Deluxe" },
            result.Books.Select(b => b.Title).ToList());
        Assert.AreEqual(2, result.Books.Count(b => b.IsCompilation));
    }

    [TestMethod]
    public async Task GetSeriesBooks_NeverCollapsesNullOrUnnumberedPositions()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Misc",
                  "slug": "misc",
                  "book_series": [
                    { "position": null, "compilation": false, "book": { "id": 1, "title": "Book A", "slug": "a", "release_date": null, "compilation": false, "users_count": 10 } },
                    { "position": null, "compilation": false, "book": { "id": 2, "title": "Book B", "slug": "b", "release_date": null, "compilation": false, "users_count": 20 } },
                    { "position": "Book One", "compilation": false, "book": { "id": 3, "title": "Book C", "slug": "c", "release_date": null, "compilation": false, "users_count": 30 } },
                    { "position": "2a", "compilation": false, "book": { "id": 4, "title": "Book D", "slug": "d", "release_date": null, "compilation": false, "users_count": 40 } }
                  ]
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);

        var result = await target.GetSeriesBooks("1");

        // An unnumbered entry is never grouped with another - neither one with a null position
        // nor one with a non-numeric string label (which never parses as a number): emitting
        // four separate rows (unlike SQL distinct_on: position, which would collapse these into
        // one).
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(
            new[] { "Book A", "Book B", "Book C", "Book D" },
            result.Books.Select(b => b.Title).ToList());
        Assert.AreEqual(4, result.Books.Count);
    }

    [TestMethod]
    public async Task GetSeriesBooks_TieOnUsersCount_KeepsLowerBookId()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Tie",
                  "slug": "tie",
                  "book_series": [
                    { "position": 1, "compilation": false, "book": { "id": 100, "title": "Alpha", "slug": "alpha", "release_date": null, "compilation": false, "users_count": 500 } },
                    { "position": 1, "compilation": false, "book": { "id": 200, "title": "Beta", "slug": "beta", "release_date": null, "compilation": false, "users_count": 500 } }
                  ]
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);

        var result = await target.GetSeriesBooks("1");

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Books.Count);
        Assert.AreEqual("Alpha", result.Books.Single().Title);
    }

    [TestMethod]
    public async Task GetSeriesBooks_MissingUsersCountRanksBelowAnyPresent()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Reacher",
                  "slug": "reacher",
                  "book_series": [
                    { "position": 1, "compilation": false, "book": { "id": 1, "title": "No Count", "slug": "no-count", "release_date": null, "compilation": false } },
                    { "position": 1, "compilation": false, "book": { "id": 2, "title": "With Count", "slug": "with-count", "release_date": null, "compilation": false, "users_count": 8 } }
                  ]
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);

        var result = await target.GetSeriesBooks("1");

        // Same position, one entry with no users_count property and one with a value: the entry
        // WITH users_count wins (a missing count ranks below any present one).
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Books.Count);
        Assert.AreEqual("With Count", result.Books.Single().Title);
    }

    [TestMethod]
    public async Task GetSeriesBooks_MissingBookIdRanksLastOnEqualUsersCount()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Reacher",
                  "slug": "reacher",
                  "book_series": [
                    { "position": 1, "compilation": false, "book": { "id": "not-a-number", "title": "No Usable Id", "slug": "no-id", "release_date": null, "compilation": false, "users_count": 500 } },
                    { "position": 1, "compilation": false, "book": { "id": 200, "title": "Usable Id", "slug": "usable-id", "release_date": null, "compilation": false, "users_count": 500 } }
                  ]
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);

        var result = await target.GetSeriesBooks("1");

        // Equal users_count: the entry with a usable numeric id wins; a missing id ranks last.
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Books.Count);
        Assert.AreEqual("Usable Id", result.Books.Single().Title);
    }

    [TestMethod]
    public async Task GetSeriesBooks_QueryRequestsPartialBookFilterAndUsersCount()
    {
        var seriesResponse = """
            {
              "data": {
                "series_by_pk": {
                  "id": 1,
                  "name": "Reacher",
                  "slug": "reacher",
                  "book_series": []
                }
              }
            }
            """;
        var target = CreateScraper(seriesResponse, out var handler);
        await target.GetSeriesBooks("1");

        var query = ExtractGraphqlQuery(handler.CapturedRequestBodies.Single());
        Assert.IsTrue(query.Contains("is_partial_book"), "id path should filter out partial editions");
        Assert.IsTrue(query.Contains("users_count"), "id path should request users_count for the dedupe");

        // And the slug-based path sends the same shape.
        var slugResponse = """
            {
              "data": {
                "series": [
                  {
                    "id": 1,
                    "name": "Reacher",
                    "slug": "reacher",
                    "book_series": []
                  }
                ]
              }
            }
            """;
        var target2 = CreateScraper(slugResponse, out var handler2);
        await target2.GetSeriesBooks("https://hardcover.app/series/reacher");

        var query2 = ExtractGraphqlQuery(handler2.CapturedRequestBodies.Single());
        Assert.IsTrue(query2.Contains("is_partial_book"), "slug path should filter out partial editions");
        Assert.IsTrue(query2.Contains("users_count"), "slug path should request users_count for the dedupe");
    }

    private static string ExtractGraphqlQuery(string requestBody)
    {
        // Parse out just the "query" field so we inspect the actual GraphQL query text sent
        // over the wire (not variable values that might coincidentally contain the substring).
        var parsed = JsonDocument.Parse(requestBody);
        return parsed.RootElement.GetProperty("query").GetString() ?? "";
    }

    private static void AssertNoDisabledOperators(string requestBody)
    {
        // Parse out just the "query" field so we inspect the actual GraphQL query text sent
        // over the wire (not variable values that might coincidentally contain the substring).
        var query = ExtractGraphqlQuery(requestBody);

        foreach (var op in _disabledOperators)
        {
            Assert.IsFalse(
                query.Contains(op, StringComparison.OrdinalIgnoreCase),
                $"query must never use the disabled Hasura operator '{op}' (see CLAUDE.md Hardcover limitations): {query}");
        }
    }
}
