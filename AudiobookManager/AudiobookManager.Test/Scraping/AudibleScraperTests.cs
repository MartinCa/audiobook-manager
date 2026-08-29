using System.Linq;
using System.Net;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class AudibleScraperTests
{
    /// <summary>
    /// Stands in for the default (unnamed) HttpClient AudibleScraper resolves via
    /// IHttpClientFactory.CreateClient(). Routes each request to a canned HTML response based
    /// on the request URI, so pagination tests can serve different content per page.
    /// </summary>
    private class FakeAudibleHandler : HttpMessageHandler
    {
        private readonly Func<Uri, string> _responseSelector;

        public FakeAudibleHandler(Func<Uri, string> responseSelector)
        {
            _responseSelector = responseSelector;
        }

        public List<Uri> RequestedUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            var html = _responseSelector(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
            });
        }
    }

    private static AudibleScraper CreateScraper(FakeAudibleHandler? handler = null)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        if (handler is not null)
        {
            var httpClient = new HttpClient(handler);
            httpClientFactory.Setup(f => f.CreateClient(string.Empty)).Returns(httpClient);
        }

        var bookSeriesMapper = new Mock<IBookSeriesMapper>();
        bookSeriesMapper.Setup(x => x.MapBookSeries(It.IsAny<IList<MetadataSeriesSearchResult>>())).Returns<IList<MetadataSeriesSearchResult>>(x => Task.FromResult(x));
        var logger = new Mock<ILogger<AudibleScraper>>();

        return new AudibleScraper(httpClientFactory.Object, bookSeriesMapper.Object, logger.Object);
    }

    [TestMethod]
    public async Task ParseAudibleDetails_TheCompleteHistoryOfScandinavia()
    {
        var target = CreateScraper();
        var html = await File.ReadAllTextAsync("Scraping/TestData/audible-scandinavia.html");
        var bookUrl = "https://www.audible.com/pd/The-Complete-History-of-Scandinavia-Audiobook/B096STBWDH";

        var result = await target.ParseAudibleDetails(html, bookUrl);

        Assert.IsNotNull(result);

        // Name
        Assert.AreEqual("The Complete History of Scandinavia", result.BookName);

        // Subtitle
        Assert.AreEqual("Covering Finland, Denmark, Sweden, Norway, Iceland, Vikings, and More", result.Subtitle);

        // Url
        Assert.AreEqual(bookUrl, result.Url);

        // Authors
        Assert.AreEqual(1, result.Authors.Count);
        Assert.AreEqual("Christopher Hughes", result.Authors.Single().Name);

        // Narrators
        Assert.AreEqual(1, result.Narrators.Count);
        Assert.AreEqual("Thomas Rode", result.Narrators.Single().Name);

        // Duration
        Assert.AreEqual("8 hrs and 37 mins", result.Duration);

        // Year
        Assert.AreEqual(2021, result.Year);

        // ImageUrl
        Assert.AreEqual("https://m.media-amazon.com/images/I/514h2Rz+xBS._SL500_.jpg", result.ImageUrl);

        // Description - should be sanitized (no HTML tags)
        Assert.IsTrue(result.Description!.Contains("Explore Scandinavia"));
        Assert.IsFalse(result.Description.Contains("<"));

        // Genres - from the topic tag chips
        Assert.AreEqual(4, result.Genres.Count);
        Assert.IsTrue(result.Genres.Contains("Scandinavia"));
        Assert.IsTrue(result.Genres.Contains("Norway History"));

        // Rating
        Assert.IsTrue(Math.Abs(4.19 - result.Rating!.Value) <= 0.01);

        // NumberOfRatings
        Assert.AreEqual(26, result.NumberOfRatings);

        // Publisher / Copyright
        Assert.AreEqual("Christopher Hughes", result.Publisher);
        Assert.AreEqual("Christopher Hughes", result.Copyright);

        // ASIN - parsed from the URL
        Assert.AreEqual("B096STBWDH", result.Asin);
    }

    [TestMethod]
    public async Task ParseAudibleDetails_MissingSubtitleAndChips_DefaultsGracefully()
    {
        var target = CreateScraper();
        var html = """
            <html><body>
            <script type="application/ld+json">
            {
                "@context": "http://schema.org",
                "@type": "Audiobook",
                "name": "A Standalone Book",
                "author": [ { "@type": "Person", "name": "Some Author" } ],
                "duration": "PT45M"
            }
            </script>
            </body></html>
            """;
        var bookUrl = "https://www.audible.com/pd/A-Standalone-Book-Audiobook/B0STANDALONE";

        var result = await target.ParseAudibleDetails(html, bookUrl);

        Assert.AreEqual("A Standalone Book", result.BookName);
        Assert.IsNull(result.Subtitle);
        Assert.AreEqual(0, result.Genres.Count);
        Assert.AreEqual(0, result.Narrators.Count);
        Assert.AreEqual("45 mins", result.Duration);
        Assert.AreEqual("B0STANDALONE", result.Asin);
    }

    [TestMethod]
    public async Task ParseAudibleDetails_NoAudiobookLdJson_ThrowsException()
    {
        var target = CreateScraper();
        var html = "<html><body><p>Not a book page</p></body></html>";

        await Assert.ThrowsExactlyAsync<Exception>(
            () => target.ParseAudibleDetails(html, "https://www.audible.com/pd/Not-A-Book/B0000000"));
    }

    // Regression test: on the current Audible detail-page markup, the series line ("Jack Reacher, Book 1")
    // is not rendered as the old `li.bc-list-item.seriesLabel` link markup - it only exists as JSON inside
    // a <script> tag nested under <adbl-product-details>. Before the fix, ParseAudibleDetails only looked
    // for the legacy markup and always returned an empty series list for pages using the new layout.
    [TestMethod]
    public async Task ParseAudibleDetails_KillingFloor_ParsesSeriesFromDetailsJson()
    {
        var target = CreateScraper();
        var html = await File.ReadAllTextAsync("Scraping/TestData/audible-killing-floor.html");
        var bookUrl = "https://www.audible.com/pd/Killing-Floor-Audiobook/B015RQON6I";

        var result = await target.ParseAudibleDetails(html, bookUrl);

        Assert.AreEqual("Killing Floor", result.BookName);

        Assert.IsNotNull(result.Series);
        Assert.AreEqual(1, result.Series.Count);
        Assert.AreEqual("Jack Reacher", result.Series.Single().SeriesName);
        Assert.AreEqual("1", result.Series.Single().SeriesPart);
    }

    [TestMethod]
    public void SupportsSeriesLookup_IsTrue()
    {
        Assert.IsTrue(CreateScraper().SupportsSeriesLookup);
    }

    [TestMethod]
    public async Task SearchSeries_JackReacher_ReturnsDeduplicatedSeriesWithAuthors()
    {
        var html = await File.ReadAllTextAsync("Scraping/TestData/audible-search-series.html");
        var handler = new FakeAudibleHandler(_ => html);
        var target = CreateScraper(handler);

        var results = await target.SearchSeries("jack reacher");

        // Two of the three search hits reference the same series (via differently-tracked
        // URLs) - they must collapse to a single result keyed by series id, and the
        // standalone book (no series link at all) must not produce a phantom entry.
        Assert.AreEqual(1, results.Count);

        var series = results.Single();
        Assert.AreEqual("B005NASKDG", series.SourceId);
        Assert.AreEqual("Jack Reacher", series.SeriesName);
        Assert.AreEqual("https://www.audible.com/series/Jack-Reacher-Audiobooks/B005NASKDG", series.SourceUrl);
        Assert.IsTrue(series.Authors.Contains("Lee Child"));
    }

    [TestMethod]
    public async Task SearchSeries_BlankSearchTerm_ReturnsEmptyWithoutFetching()
    {
        var handler = new FakeAudibleHandler(_ => throw new InvalidOperationException("Should not fetch for a blank search term"));
        var target = CreateScraper(handler);

        var results = await target.SearchSeries("   ");

        Assert.AreEqual(0, results.Count);
    }

    // Regression test: a series page lists an "alternate edition/narration" row directly after
    // each numbered book - same cover, its own heading is just the title text rather than
    // "Book N". Before filtering on the heading pattern, naively parsing every productListItem
    // roster row would have added that duplicate to the roster as if it were its own series
    // entry (with a null Position, since it has none), doubling up "Killing Floor". This also
    // exercises pagination: the roster spans two fetched pages and stops once the last page's
    // "next" button is disabled.
    [TestMethod]
    public async Task GetSeriesBooks_JackReacher_PaginatesAndExcludesAlternateEditions()
    {
        var page1 = await File.ReadAllTextAsync("Scraping/TestData/audible-series-page1.html");
        var page2 = await File.ReadAllTextAsync("Scraping/TestData/audible-series-page2.html");

        var handler = new FakeAudibleHandler(uri =>
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            var page = query.TryGetValue("page", out var values) ? values.ToString() : "1";
            return page == "2" ? page2 : page1;
        });
        var target = CreateScraper(handler);

        var result = await target.GetSeriesBooks("B005NASKDG");

        Assert.IsNotNull(result);
        Assert.AreEqual("B005NASKDG", result!.SourceId);
        Assert.AreEqual("Jack Reacher", result.SeriesName);
        Assert.AreEqual("https://www.audible.com/series/Jack-Reacher-Audiobooks/B005NASKDG", result.SourceUrl);

        // Exactly the two numbered entries - the alternate-edition row on page 1 must not appear.
        Assert.AreEqual(2, result.Books.Count);
        Assert.AreEqual(2, result.BookCount);

        var book1 = result.Books[0];
        Assert.AreEqual("Killing Floor", book1.Title);
        Assert.AreEqual("1", book1.Position);
        Assert.AreEqual(2015, book1.Year);
        Assert.AreEqual("https://www.audible.com/pd/Killing-Floor-Audiobook/B015RQON6I?ref=a_series_c5_1", book1.SourceUrl);
        Assert.IsFalse(book1.IsCompilation);

        var book2 = result.Books[1];
        Assert.AreEqual("Die Trying", book2.Title);
        Assert.AreEqual("2", book2.Position);
        Assert.AreEqual(2017, book2.Year);

        // Confirms the loop actually paginated (fetched both page 1 and page 2) rather than
        // only ever hitting page 1.
        Assert.IsTrue(handler.RequestedUris.Any(u => QueryHelpers.ParseQuery(u.Query).TryGetValue("page", out var v) && v.ToString() == "2"));
    }

    [TestMethod]
    public async Task GetSeriesBooks_SeriesNotFound_ReturnsNull()
    {
        var handler = new FakeAudibleHandler(_ => "<html><body><p>Not a series page</p></body></html>");
        var target = CreateScraper(handler);

        var result = await target.GetSeriesBooks("B0NONEXISTENT");

        Assert.IsNull(result);
    }
}
