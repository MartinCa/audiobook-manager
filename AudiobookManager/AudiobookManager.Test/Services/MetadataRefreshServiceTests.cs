using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
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

    /// <summary>The single settings row with no inter-item delay, so the bulk loop never sleeps.</summary>
    private static Database.Models.LibrarySettings ZeroDelaySettings() =>
        new Database.Models.LibrarySettings() { MetadataRefreshDelayMs = 0 };

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

    #region RefreshSelectedAudiobooksAsync

    // Unlike the stale sweep - which only ever loads refreshable books - the user explicitly
    // picked a non-refreshable book here, so "no source URL" is a result to report, not a row to
    // drop: 1 of 1 processed, the same book failed.
    [TestMethod]
    public async Task RefreshSelectedAudiobooksAsync_ANonRefreshableBook_IsCountedFailed_NotDroppedFromTheTotal()
    {
        var book = Book("https://www.example.com/unsupported");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 42 }))
            .ReturnsAsync(new List<Database.Models.Audiobook> { book });
        _librarySettingsRepository.Setup(r => r.GetOrCreateAsync())
            .ReturnsAsync(ZeroDelaySettings());

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();
        var result = await CreateService().RefreshSelectedAudiobooksAsync(
            new List<long> { 42 },
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual(new MetadataRefreshBatchResult(1, 1, 0, 1, StopReason: null), result);
        Assert.AreEqual((1, 1, 0, 1), progressCalls.Last());
        _scrapingService.Verify(s => s.GetBookDetails(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshSelectedAudiobooksAsync_IdsThatDoNotResolve_AreCountedFailed()
    {
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 99, 100 }))
            .ReturnsAsync(new List<Database.Models.Audiobook>());
        _librarySettingsRepository.Setup(r => r.GetOrCreateAsync())
            .ReturnsAsync(ZeroDelaySettings());

        var result = await CreateService().RefreshSelectedAudiobooksAsync(
            new List<long> { 99, 100 }, (_, _, _, _) => Task.CompletedTask);

        Assert.AreEqual(new MetadataRefreshBatchResult(2, 2, 0, 2, StopReason: null), result, "every requested id counts, resolved or not");
    }

    [TestMethod]
    public async Task RefreshSelectedAudiobooksAsync_RefreshesTheRefreshableBook_AndCountsEveryRequestedId()
    {
        var refreshable = Book("https://www.audible.com/pd/whatever");
        var unsupported = Book("https://www.example.com/unsupported");
        unsupported.Id = 43;
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 42, 43 }))
            .ReturnsAsync(new List<Database.Models.Audiobook> { refreshable, unsupported });
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(42)).ReturnsAsync(refreshable);
        _librarySettingsRepository.Setup(r => r.GetOrCreateAsync())
            .ReturnsAsync(ZeroDelaySettings());
        _pendingRepository.Setup(r => r.DeleteByAudiobookIdAsync(42)).ReturnsAsync(true);
        _audiobookRepository.Setup(r => r.UpdateLastMetadataRefreshedAtAsync(42, It.IsAny<DateTime>()))
            .Returns(Task.CompletedTask);

        var scraper = new Mock<IScraper>();
        scraper.Setup(s => s.SupportsUrl(refreshable.Www!)).Returns(true);
        scraper.Setup(s => s.RequiresApiKey).Returns(false);
        _scrapingService.Setup(s => s.GetBookDetails(refreshable.Www!))
            .ReturnsAsync(new MetadataSearchResult("https://www.audible.com/pd/whatever", "A Book")
            {
                Source = "Audible",
                Authors = new List<AudiobookManager.Domain.Person>(),
                Narrators = new List<AudiobookManager.Domain.Person>(),
                Genres = new List<string>()
            });

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();
        var result = await CreateService(new[] { scraper.Object }).RefreshSelectedAudiobooksAsync(
            new List<long> { 42, 43 },
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual(new MetadataRefreshBatchResult(2, 2, 1, 1, StopReason: null), result);
        Assert.AreEqual(2, progressCalls.Count);
        Assert.AreEqual((1, 2, 1, 0), progressCalls[0]);
        Assert.AreEqual((2, 2, 1, 1), progressCalls[1]);
        _audiobookRepository.Verify(
            r => r.UpdateLastMetadataRefreshedAtAsync(42, It.IsAny<DateTime>()), Times.Once);
    }

    [TestMethod]
    public async Task RefreshSelectedAudiobooksAsync_HardcoverBudgetExhausted_ReportsStopReason_AndSkipsTheRemainingBooks()
    {
        // Regression for the review finding: the loop set stopReason internally but dropped it
        // from the return value, so the controller's MetadataRefreshComplete always carried no
        // stop reason - a Hardcover daily-limit halt toasted as a plain success/failure summary.
        // The stop reason must travel out of the batch method, and the books after the exhausted
        // one must not be processed (each costs a request the budget no longer grants).
        var first = Book("https://www.audible.com/pd/first");
        var budgetExhausted = Book("https://www.audible.com/pd/exhausted");
        budgetExhausted.Id = 43;
        var neverTouched = Book("https://www.audible.com/pd/never");
        neverTouched.Id = 44;
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 42, 43, 44 }))
            .ReturnsAsync(new List<Database.Models.Audiobook> { first, budgetExhausted, neverTouched });
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(42)).ReturnsAsync(first);
        _librarySettingsRepository.Setup(r => r.GetOrCreateAsync())
            .ReturnsAsync(ZeroDelaySettings());
        _pendingRepository.Setup(r => r.DeleteByAudiobookIdAsync(42)).ReturnsAsync(true);
        _audiobookRepository.Setup(r => r.UpdateLastMetadataRefreshedAtAsync(42, It.IsAny<DateTime>()))
            .Returns(Task.CompletedTask);
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(43)).ReturnsAsync(budgetExhausted);

        var scraper = new Mock<IScraper>();
        scraper.Setup(s => s.SupportsUrl(It.IsAny<string>())).Returns(true);
        scraper.Setup(s => s.RequiresApiKey).Returns(false);
        _scrapingService.Setup(s => s.GetBookDetails(first.Www!))
            .ReturnsAsync(new MetadataSearchResult(first.Www!, "First")
            {
                Source = "Audible",
                Authors = new List<AudiobookManager.Domain.Person>(),
                Narrators = new List<AudiobookManager.Domain.Person>(),
                Genres = new List<string>()
            });
        _scrapingService.Setup(s => s.GetBookDetails(budgetExhausted.Www!))
            .ThrowsAsync(new HardcoverDailyLimitExceededException(5000));

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();
        var result = await CreateService(new[] { scraper.Object }).RefreshSelectedAudiobooksAsync(
            new List<long> { 42, 43, 44 },
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual(new MetadataRefreshBatchResult(1, 3, 1, 0, "Hardcover daily API request limit reached"), result);
        // One progress event per processed book - the third never happens because the loop stops.
        Assert.AreEqual(1, progressCalls.Count);
        Assert.AreEqual((1, 3, 1, 0), progressCalls[0]);
        // Book 44 was never fetched, so the daily budget was not spent on anything after the stop.
        _scrapingService.Verify(s => s.GetBookDetails(It.IsAny<string>()), Times.Exactly(2));
        _audiobookRepository.Verify(
            r => r.GetByIdWithIncludesAsync(It.IsAny<long>()), Times.Exactly(2));
    }

    #endregion
}