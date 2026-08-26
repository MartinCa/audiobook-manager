using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using Moq;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class AudibleScraperTests
{
    private static AudibleScraper CreateScraper()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var bookSeriesMapper = new Mock<IBookSeriesMapper>();
        bookSeriesMapper.Setup(x => x.MapBookSeries(It.IsAny<IList<BookSeriesSearchResult>>())).Returns<IList<BookSeriesSearchResult>>(x => Task.FromResult(x));

        return new AudibleScraper(httpClientFactory.Object, bookSeriesMapper.Object);
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
}
