using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class ScrapingServiceTests
{
    private Mock<IScraper> _audibleScraper = null!;
    private Mock<IScraper> _goodreadsScraper = null!;
    private Mock<ILogger<ScrapingService>> _logger = null!;
    private ScrapingService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audibleScraper = CreateScraperMock("Audible");
        _goodreadsScraper = CreateScraperMock("Goodreads");
        _logger = new Mock<ILogger<ScrapingService>>();

        _service = new ScrapingService(
            new[] { _audibleScraper.Object, _goodreadsScraper.Object },
            _logger.Object);
    }

    private static Mock<IScraper> CreateScraperMock(string sourceName)
    {
        var mock = new Mock<IScraper>();
        mock.Setup(s => s.SourceName).Returns(sourceName);
        mock.Setup(s => s.IsSource(It.IsAny<string>()))
            .Returns((string name) => string.Equals(name, sourceName, StringComparison.InvariantCultureIgnoreCase));
        return mock;
    }

    [TestMethod]
    public async Task SearchMultiple_OneSourceFailsAndOneSucceeds_ReturnsPartialResultsWithStatuses()
    {
        _goodreadsScraper.Setup(s => s.Search("some book"))
            .ReturnsAsync(new List<MetadataSearchResult>
            {
                new("https://goodreads.com/book/1", "Some Book"),
            });
        _audibleScraper.Setup(s => s.Search("some book"))
            .ThrowsAsync(new Exception("Audible timed out"));

        var result = await _service.SearchMultiple(new[] { "Audible", "Goodreads" }, "some book");

        Assert.AreEqual(1, result.Results.Count);
        Assert.AreEqual("Goodreads", result.Results[0].Source);

        Assert.AreEqual(2, result.SourceStatuses.Count);

        var audibleStatus = result.SourceStatuses.Single(s => s.Source == "Audible");
        Assert.IsFalse(audibleStatus.Success);
        Assert.AreEqual(0, audibleStatus.ResultCount);
        Assert.AreEqual("Audible timed out", audibleStatus.Error);

        var goodreadsStatus = result.SourceStatuses.Single(s => s.Source == "Goodreads");
        Assert.IsTrue(goodreadsStatus.Success);
        Assert.AreEqual(1, goodreadsStatus.ResultCount);
        Assert.IsNull(goodreadsStatus.Error);
    }

    [TestMethod]
    public async Task SearchMultiple_SourceReturnsNoResults_ReportsSuccessWithZeroCount()
    {
        _goodreadsScraper.Setup(s => s.Search("nothing")).ReturnsAsync(new List<MetadataSearchResult>());
        _audibleScraper.Setup(s => s.Search("nothing")).ReturnsAsync(new List<MetadataSearchResult>());

        var result = await _service.SearchMultiple(new[] { "Audible", "Goodreads" }, "nothing");

        Assert.AreEqual(0, result.Results.Count);
        Assert.IsTrue(result.SourceStatuses.All(s => s.Success && s.ResultCount == 0));
    }

    [TestMethod]
    public async Task GetBookDetails_UrlMatchesExactlyOneScraper_ReturnsThatScrapersDetailsTaggedWithItsSource()
    {
        const string url = "https://goodreads.com/book/1";
        _audibleScraper.Setup(s => s.SupportsUrl(url)).Returns(false);
        _goodreadsScraper.Setup(s => s.SupportsUrl(url)).Returns(true);
        _goodreadsScraper.Setup(s => s.GetBookDetails(url))
            .ReturnsAsync(new MetadataSearchResult(url, "Some Book"));

        var result = await _service.GetBookDetails(url);

        Assert.AreEqual("Goodreads", result.Source);
        _audibleScraper.Verify(s => s.GetBookDetails(It.IsAny<string>()), Times.Never);
    }

    // Regression test: the URL-entry path reaches every registered scraper's SupportsUrl(),
    // bypassing the "enabled" check GetSearchServiceInfo() applies for the source picker (a
    // scraper whose RequiresApiKey is true and whose key is not configured shows as disabled
    // there). Pasting a URL for a matching-but-disabled source used to fall through to the
    // scraper's own GetBookDetails() call, which fails in whatever unhelpful way that scraper
    // fails without a key, instead of the same clear "not configured" reason the source picker
    // already shows.
    [TestMethod]
    public async Task GetBookDetails_UrlMatchesScraperWithUnconfiguredApiKey_ThrowsArgumentExceptionNamingTheSource()
    {
        const string url = "https://hardcover.app/books/1984";
        _audibleScraper.Setup(s => s.SupportsUrl(url)).Returns(false);
        _goodreadsScraper.Setup(s => s.SupportsUrl(url)).Returns(false);

        var hardcoverScraper = CreateScraperMock("Hardcover");
        hardcoverScraper.Setup(s => s.SupportsUrl(url)).Returns(true);
        hardcoverScraper.Setup(s => s.RequiresApiKey).Returns(true);
        hardcoverScraper.Setup(s => s.IsApiKeyConfigured).Returns(false);

        var service = new ScrapingService(
            new[] { _audibleScraper.Object, _goodreadsScraper.Object, hardcoverScraper.Object },
            _logger.Object);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GetBookDetails(url));

        StringAssert.Contains(ex.Message, "Hardcover");
        hardcoverScraper.Verify(s => s.GetBookDetails(It.IsAny<string>()), Times.Never);
    }

    // Regression test: BookSearchDialog's "paste a book URL" entry point (see AGENTS.md's
    // "Adding a metadata source scraper" section) used to send the raw URL string into the
    // multi-source *text* search instead of calling GetBookDetails, so a URL from an unconfigured
    // or unsupported source silently produced zero results with no explanation. GetBookDetails is
    // the single place that decides "which source supports this URL", and it must fail with a
    // message a caller can act on - not a raw exception that becomes an opaque 500 - when nothing
    // does.
    [TestMethod]
    public async Task GetBookDetails_NoScraperSupportsUrl_ThrowsArgumentExceptionNamingTheUrl()
    {
        const string url = "https://unsupported-source.example/books/1";
        _audibleScraper.Setup(s => s.SupportsUrl(url)).Returns(false);
        _goodreadsScraper.Setup(s => s.SupportsUrl(url)).Returns(false);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => _service.GetBookDetails(url));

        StringAssert.Contains(ex.Message, url);
    }
}
