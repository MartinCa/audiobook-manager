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
            .ReturnsAsync(new List<BookSearchResult>
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
        _goodreadsScraper.Setup(s => s.Search("nothing")).ReturnsAsync(new List<BookSearchResult>());
        _audibleScraper.Setup(s => s.Search("nothing")).ReturnsAsync(new List<BookSearchResult>());

        var result = await _service.SearchMultiple(new[] { "Audible", "Goodreads" }, "nothing");

        Assert.AreEqual(0, result.Results.Count);
        Assert.IsTrue(result.SourceStatuses.All(s => s.Success && s.ResultCount == 0));
    }
}
