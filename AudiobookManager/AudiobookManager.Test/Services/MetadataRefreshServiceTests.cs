using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataRefreshServiceTests
{
    private readonly Mock<IAudiobookRepository> _audiobookRepository = new();
    private readonly Mock<IPendingMetadataRefreshRepository> _pendingRepository = new();
    private readonly Mock<IConsistencyIssueRepository> _issueRepository = new();
    private readonly Mock<IScrapingService> _scrapingService = new();
    private readonly Mock<ILibrarySettingsRepository> _librarySettingsRepository = new();
    private readonly Mock<ILogger<MetadataRefreshService>> _logger = new();

    private MetadataRefreshService CreateService(IEnumerable<IScraper>? scrapers = null) =>
        new(
            _audiobookRepository.Object,
            _pendingRepository.Object,
            _issueRepository.Object,
            _scrapingService.Object,
            scrapers ?? Array.Empty<IScraper>(),
            _librarySettingsRepository.Object,
            _logger.Object);

    private static Database.Models.Audiobook Book(string www) => new(
        42, "A Book", null, null, null, 2024,
        null, null, null, null, null, null, null, null, null,
        "/library/book.m4b", "book.m4b", 1000)
    { Www = www };

    [TestMethod]
    public async Task RefreshAudiobookAsync_ConcurrentResolveDeletedTheStaleIssue_FailureIsSwallowed()
    {
        // Regression (PR #1380 review round 2): the stale-issue update happens after a no-tracking
        // read. If a concurrent resolve deletes the issue in between, UpdateAsync's fail-fast
        // (KeyNotFoundException) used to propagate out of RefreshAudiobookAsync's own catch block,
        // and the single-book endpoint mapped it to NotFound() - reporting "book not found" for a
        // book that exists, over pure bookkeeping. The failure write is best-effort.
        var book = Book("https://www.audible.com/pd/whatever");
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(42)).ReturnsAsync(book);

        var scraper = new Mock<IScraper>();
        scraper.Setup(s => s.SupportsUrl(book.Www!)).Returns(true);
        scraper.Setup(s => s.RequiresApiKey).Returns(false);
        _scrapingService.Setup(s => s.GetBookDetails(book.Www!))
            .ThrowsAsync(new HttpRequestException("network down"));

        var stale = new ConsistencyIssue
        {
            Id = 7, AudiobookId = 42,
            IssueType = ConsistencyIssueType.MetadataRefreshFailed,
            Description = "Metadata refresh failed",
            DetectedAt = DateTime.UtcNow,
        };
        _issueRepository.Setup(r => r.GetByAudiobookIdAsync(42))
            .ReturnsAsync(new List<ConsistencyIssue> { stale });
        _issueRepository
            .Setup(r => r.UpdateAsync(It.IsAny<ConsistencyIssue>()))
            .ThrowsAsync(new KeyNotFoundException("Consistency issue 7 does not exist."));

        var result = await CreateService(new[] { scraper.Object }).RefreshAudiobookAsync(42);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("network down", result.Error);
        _issueRepository.Verify(r => r.InsertAsync(It.IsAny<ConsistencyIssue>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshAudiobookAsync_NoStaleIssue_InsertsFreshFailureRow()
    {
        var book = Book("https://www.audible.com/pd/whatever");
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(42)).ReturnsAsync(book);

        var scraper = new Mock<IScraper>();
        scraper.Setup(s => s.SupportsUrl(book.Www!)).Returns(true);
        scraper.Setup(s => s.RequiresApiKey).Returns(false);
        _scrapingService.Setup(s => s.GetBookDetails(book.Www!))
            .ThrowsAsync(new HttpRequestException("network down"));

        _issueRepository.Setup(r => r.GetByAudiobookIdAsync(42))
            .ReturnsAsync(new List<ConsistencyIssue>());

        var result = await CreateService(new[] { scraper.Object }).RefreshAudiobookAsync(42);

        Assert.IsFalse(result.Success);
        _issueRepository.Verify(
            r => r.InsertAsync(It.Is<ConsistencyIssue>(i =>
                i.AudiobookId == 42 &&
                i.IssueType == ConsistencyIssueType.MetadataRefreshFailed &&
                i.ActualValue == "network down")),
            Times.Once);
    }

    [TestMethod]
    public async Task RefreshAudiobookAsync_LastMetadataRefreshedAt_NotStampedOnFailure()
    {
        var book = Book("https://www.audible.com/pd/whatever");
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(42)).ReturnsAsync(book);

        var scraper = new Mock<IScraper>();
        scraper.Setup(s => s.SupportsUrl(book.Www!)).Returns(true);
        scraper.Setup(s => s.RequiresApiKey).Returns(false);
        _scrapingService.Setup(s => s.GetBookDetails(book.Www!))
            .ThrowsAsync(new HttpRequestException("network down"));
        _issueRepository.Setup(r => r.GetByAudiobookIdAsync(42))
            .ReturnsAsync(new List<ConsistencyIssue>());

        await CreateService(new[] { scraper.Object }).RefreshAudiobookAsync(42);

        _audiobookRepository.Verify(
            r => r.UpdateLastMetadataRefreshedAtAsync(It.IsAny<long>(), It.IsAny<DateTime>()),
            Times.Never);
    }
}