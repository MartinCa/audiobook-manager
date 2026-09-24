using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataRefreshServiceTests
{
    private readonly Mock<IAudiobookRepository> _audiobookRepository = new();
    private readonly Mock<IPendingMetadataRefreshRepository> _pendingRepository = new();
    private readonly Mock<IBookConsistencyIssueRepository> _issueRepository = new();
    private readonly Mock<IScrapingService> _scrapingService = new();
    private readonly Mock<ILibrarySettingsRepository> _librarySettingsRepository = new();
    private readonly Mock<IAudiobookService> _audiobookService = new();
    private readonly IAudiobookSaveGate _saveGate = new AudiobookSaveGate();
    private readonly Mock<IBookSeriesMapper> _bookSeriesMapper = new();
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactory = new();
    private readonly Mock<ILogger<MetadataRefreshService>> _logger = new();

    public MetadataRefreshServiceTests()
    {
        // Pass-through by default: most tests here do not exercise series mapping at all, so the
        // mapper should not silently rewrite series names it was never asked to.
        _bookSeriesMapper
            .Setup(m => m.MapBookSeries(It.IsAny<IList<MetadataSeriesSearchResult>>()))
            .Returns<IList<MetadataSeriesSearchResult>>(results => Task.FromResult(results));
    }

    private MetadataRefreshService CreateService(IEnumerable<IScraper>? scrapers = null) =>
        new(
            _audiobookRepository.Object,
            _pendingRepository.Object,
            _issueRepository.Object,
            _scrapingService.Object,
            scrapers ?? Array.Empty<IScraper>(),
            _librarySettingsRepository.Object,
            _audiobookService.Object,
            _saveGate,
            _bookSeriesMapper.Object,
            _serviceScopeFactory.Object,
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

        var stale = new BookConsistencyIssue
        {
            Id = 7, AudiobookId = 42,
            IssueType = BookConsistencyIssueType.MetadataRefreshFailed,
            Description = "Metadata refresh failed",
            DetectedAt = DateTime.UtcNow,
        };
        _issueRepository.Setup(r => r.GetByAudiobookIdAsync(42))
            .ReturnsAsync(new List<BookConsistencyIssue> { stale });
        _issueRepository
            .Setup(r => r.UpdateAsync(It.IsAny<BookConsistencyIssue>()))
            .ThrowsAsync(new KeyNotFoundException("Consistency issue 7 does not exist."));

        var result = await CreateService(new[] { scraper.Object }).RefreshAudiobookAsync(42);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("network down", result.Error);
        _issueRepository.Verify(r => r.InsertAsync(It.IsAny<BookConsistencyIssue>()), Times.Never);
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
            .ReturnsAsync(new List<BookConsistencyIssue>());

        var result = await CreateService(new[] { scraper.Object }).RefreshAudiobookAsync(42);

        Assert.IsFalse(result.Success);
        _issueRepository.Verify(
            r => r.InsertAsync(It.Is<BookConsistencyIssue>(i =>
                i.AudiobookId == 42 &&
                i.IssueType == BookConsistencyIssueType.MetadataRefreshFailed &&
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
            .ReturnsAsync(new List<BookConsistencyIssue>());

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

    #region ApplyPendingRefreshAsync

    // Regression (review finding): a corrupt/unparseable stored payload used to return false,
    // indistinguishable from "book no longer exists" - both mapped to 204 at the controller, so
    // a caller could not tell "nothing to apply" from "the stored snapshot is unreadable" and
    // reported success without applying anything. It must now throw instead.
    [TestMethod]
    public async Task ApplyPendingRefreshAsync_UnparseablePayload_ThrowsInsteadOfReturningFalse()
    {
        _pendingRepository.Setup(r => r.GetByAudiobookIdAsync(99))
            .ReturnsAsync(new PendingMetadataRefresh
            {
                AudiobookId = 99,
                FetchedAt = DateTime.UtcNow,
                SourceName = "Audible",
                SourceUrl = "https://example.com/book",
                PayloadJson = "not valid json",
                ChangedFieldsJson = "[\"Rating\"]",
            });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => CreateService().ApplyPendingRefreshAsync(99));

        // Never reached the point of touching the book or the save gate.
        _audiobookRepository.Verify(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
    }

    // Behavioral assertion, not a regression guard: ApplyOneAsync never caught
    // AudiobookBusyException, so this passes with or without the fix - the actual regression was
    // the controller's missing catch, which
    // MetadataRefreshControllerTests.ApplyPending_BookBusy_Returns409NotA500 covers (and does
    // fail without that fix). This test documents that the service's contract is, and must stay,
    // "let the busy exception propagate" - the controller relies on that to map it to 409.
    [TestMethod]
    public async Task ApplyPendingRefreshAsync_BookAlreadyBusy_PropagatesAudiobookBusyException()
    {
        _pendingRepository.Setup(r => r.GetByAudiobookIdAsync(101))
            .ReturnsAsync(new PendingMetadataRefresh
            {
                AudiobookId = 101,
                FetchedAt = DateTime.UtcNow,
                SourceName = "Audible",
                SourceUrl = "https://example.com/book",
                PayloadJson = PendingRefreshPayload.Serialize(new PendingRefreshPayload.Snapshot(
                    PendingRefreshPayload.CurrentVersion,
                    "https://example.com/book",
                    "Audible",
                    new List<string> { "A Person" },
                    new List<string>(),
                    "A Book",
                    null, null, null, null,
                    new List<string>(),
                    null, null,
                    "4.5",
                    null, null, null)),
                ChangedFieldsJson = "[\"Rating\"]",
            });
        var busyBook = Book("https://example.com/book");
        busyBook.Id = 101;
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Database.Models.Audiobook> { busyBook });

        using var lease = _saveGate.Acquire(101);

        await Assert.ThrowsExactlyAsync<AudiobookBusyException>(
            () => CreateService().ApplyPendingRefreshAsync(101));
    }

    // Regression: every pending row an existing library already had on disk before this feature
    // shipped has ChangedFieldsJson stored as null (the column did not exist when those rows were
    // written). Left unhandled, that made the field filter permanently exclude every such row
    // (a subset check against an empty "changed" list never matches) and the list's per-row
    // badges stayed empty forever. The read paths must self-heal these rows by recomputing and
    // persisting the changed-field list from the row's own stored snapshot.
    [TestMethod]
    public async Task GetPendingAudiobookIdsAsync_LegacyRowMissingChangedFields_BackfillsFromStoredSnapshot()
    {
        var book = new Database.Models.Audiobook(
            200, "A Book", null, null, null, 2024,
            null, null, null, null, "4.0", null, null, null, null,
            "/library/book.m4b", "book.m4b", 1000);
        book.Authors = new List<AudiobookManager.Database.Models.Person> { new AudiobookManager.Database.Models.Person(default, "Author A") };

        _pendingRepository.Setup(r => r.GetAudiobookIdsMissingChangedFieldsAsync())
            .ReturnsAsync(new List<long> { 200 });
        _pendingRepository.Setup(r => r.GetByAudiobookIdAsync(200))
            .ReturnsAsync(new PendingMetadataRefresh
            {
                AudiobookId = 200,
                FetchedAt = DateTime.UtcNow,
                SourceName = "Audible",
                SourceUrl = "https://example.com/book",
                PayloadJson = PendingRefreshPayload.Serialize(new PendingRefreshPayload.Snapshot(
                    PendingRefreshPayload.CurrentVersion,
                    "https://example.com/book",
                    "Audible",
                    new List<string> { "Author A" },
                    new List<string>(),
                    "A Book",
                    null, null, null, null,
                    new List<string>(),
                    null, null,
                    "4.5",
                    null, null, null)),
                ChangedFieldsJson = null,
            });
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(200)).ReturnsAsync(book);
        _pendingRepository.Setup(r => r.GetPendingAudiobookIdsAsync()).ReturnsAsync(new List<long> { 200 });

        var ids = await CreateService().GetPendingAudiobookIdsAsync();

        Assert.AreSequenceEqual(new List<long> { 200 }, ids);
        // Only Rating actually differs (Authors/BookName/Year are identical) - the backfill must
        // persist exactly that, not every field the snapshot happens to carry a value for.
        _pendingRepository.Verify(
            r => r.SetChangedFieldsJsonAsync(200, "[\"Rating\"]"), Times.Once);
    }

    // Companion to the backfill test above: a caller can reach a legacy row directly through
    // apply-selected/apply-filtered before any list view has had a chance to backfill it. Before
    // the fix, an empty stored changed-fields list (with no explicit fields given either) was
    // treated as "nothing to apply" and the row was dismissed without ever touching the book.
    [TestMethod]
    public async Task ApplyPendingRefreshAsync_LegacyRowMissingChangedFields_RecomputesAndAppliesTheActualDiff()
    {
        var book = new Database.Models.Audiobook(
            300, "A Book", null, null, null, 2024,
            null, null, null, null, "4.0", null, null, null, null,
            "/library/book.m4b", "book.m4b", 1000);
        book.Authors = new List<AudiobookManager.Database.Models.Person> { new AudiobookManager.Database.Models.Person(default, "Author A") };

        _pendingRepository.Setup(r => r.GetByAudiobookIdAsync(300))
            .ReturnsAsync(new PendingMetadataRefresh
            {
                AudiobookId = 300,
                FetchedAt = DateTime.UtcNow,
                SourceName = "Audible",
                SourceUrl = "https://example.com/book",
                PayloadJson = PendingRefreshPayload.Serialize(new PendingRefreshPayload.Snapshot(
                    PendingRefreshPayload.CurrentVersion,
                    "https://example.com/book",
                    "Audible",
                    new List<string> { "Author A" },
                    new List<string>(),
                    "A Book",
                    null, null, null, null,
                    new List<string>(),
                    null, null,
                    "4.5",
                    null, null, null)),
                ChangedFieldsJson = null,
            });
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Database.Models.Audiobook> { book });

        Domain.Audiobook? captured = null;
        _audiobookService.Setup(s => s.UpdateAudiobook(300, It.IsAny<Domain.Audiobook>()))
            .Callback<long, Domain.Audiobook, Func<string, int, Task>?>((_, a, _) => captured = a)
            .ReturnsAsync((long _, Domain.Audiobook a, Func<string, int, Task>? _) => a);

        var libraryConsistencyService = new Mock<ILibraryConsistencyService>();
        libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(300))
            .ReturnsAsync(new List<Database.Models.BookConsistencyIssue>());
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(libraryConsistencyService.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var applied = await CreateService().ApplyPendingRefreshAsync(300);

        Assert.IsTrue(applied);
        Assert.IsNotNull(captured);
        Assert.AreEqual("4.5", captured!.Rating);
        _pendingRepository.Verify(r => r.DeleteByAudiobookIdAsync(300), Times.Once);
    }

    #endregion

    #region ReevaluatePendingRefreshesAsync

    private static PendingMetadataRefresh PendingRow(long audiobookId, PendingRefreshPayload.Snapshot payload, string? changedFieldsJson) =>
        new()
        {
            AudiobookId = audiobookId,
            FetchedAt = DateTime.UtcNow,
            SourceName = payload.Source,
            SourceUrl = payload.Url,
            PayloadJson = PendingRefreshPayload.Serialize(payload),
            ChangedFieldsJson = changedFieldsJson,
        };

    // Regression: a mapping pattern added after a snapshot was captured used to never take
    // effect on that snapshot - the stored SeriesName was whatever the mapper produced at fetch
    // time and was never revisited. Re-evaluating must re-run the mapper against the snapshot's
    // pre-mapping OriginalSeriesName with today's patterns and persist the corrected name.
    [TestMethod]
    public async Task ReevaluatePendingRefreshesAsync_SeriesMappingAddedSinceFetch_RemapsAndSupersedesTheDiff()
    {
        var book = new Database.Models.Audiobook(
            400, "Book Four", null, "Thursday Murder Club", "4", 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/book.m4b", "book.m4b", 1000);
        book.Authors = new List<Database.Models.Person> { new(default, "Author A") };

        var payload = new PendingRefreshPayload.Snapshot(
            PendingRefreshPayload.CurrentVersion,
            "https://example.com/book",
            "Audible",
            new List<string> { "Author A" },
            new List<string>(),
            "Book Four",
            null,
            "A Thursday Murder Club Mystery", // the unmapped name stored before the pattern existed
            "4",
            null,
            new List<string>(),
            null, null, null, null, null, null,
            "A Thursday Murder Club Mystery"); // OriginalSeriesName - what the source actually reported

        _pendingRepository.Setup(r => r.GetPendingAudiobookIdsAsync()).ReturnsAsync(new List<long> { 400 });
        _pendingRepository.Setup(r => r.GetByAudiobookIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(new List<PendingMetadataRefresh> { PendingRow(400, payload, "[\"Series\"]") });
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Database.Models.Audiobook> { book });

        _bookSeriesMapper
            .Setup(m => m.MapBookSeries(It.IsAny<IList<MetadataSeriesSearchResult>>()))
            .Returns<IList<MetadataSeriesSearchResult>>(results =>
                Task.FromResult<IList<MetadataSeriesSearchResult>>(results
                    .Select(r => new MetadataSeriesSearchResult("Thursday Murder Club") { SeriesPart = r.SeriesPart })
                    .ToList()));

        var result = await CreateService().ReevaluatePendingRefreshesAsync();

        Assert.AreEqual(1, result.Processed);
        Assert.AreEqual(0, result.Updated);
        Assert.AreEqual(1, result.Removed);
        // The remapped series now agrees with the book, so nothing differs any more: the row is
        // dismissed rather than left showing a change the user cannot want.
        _pendingRepository.Verify(r => r.DeleteByAudiobookIdAsync(400), Times.Once);
        _pendingRepository.Verify(
            r => r.UpdatePayloadAndChangedFieldsAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ReevaluatePendingRefreshesAsync_RemapStillDiffers_UpdatesStoredPayloadAndChangedFields()
    {
        var book = new Database.Models.Audiobook(
            401, "Book Five", null, "Old Series Name", "5", 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/book.m4b", "book.m4b", 1000);
        book.Authors = new List<Database.Models.Person> { new(default, "Author A") };

        var payload = new PendingRefreshPayload.Snapshot(
            PendingRefreshPayload.CurrentVersion,
            "https://example.com/book",
            "Audible",
            new List<string> { "Author A" },
            new List<string>(),
            "Book Five",
            null,
            "Raw Source Series Name",
            "5",
            null,
            new List<string>(),
            null, null, null, null, null, null,
            "Raw Source Series Name");

        _pendingRepository.Setup(r => r.GetPendingAudiobookIdsAsync()).ReturnsAsync(new List<long> { 401 });
        _pendingRepository.Setup(r => r.GetByAudiobookIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(new List<PendingMetadataRefresh> { PendingRow(401, payload, "[\"Series\"]") });
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Database.Models.Audiobook> { book });

        _bookSeriesMapper
            .Setup(m => m.MapBookSeries(It.IsAny<IList<MetadataSeriesSearchResult>>()))
            .Returns<IList<MetadataSeriesSearchResult>>(results =>
                Task.FromResult<IList<MetadataSeriesSearchResult>>(results
                    .Select(r => new MetadataSeriesSearchResult("Newly Mapped Series") { SeriesPart = r.SeriesPart })
                    .ToList()));

        var result = await CreateService().ReevaluatePendingRefreshesAsync();

        Assert.AreEqual(1, result.Processed);
        Assert.AreEqual(1, result.Updated);
        Assert.AreEqual(0, result.Removed);
        _pendingRepository.Verify(
            r => r.UpdatePayloadAndChangedFieldsAsync(
                401,
                It.Is<string>(json => json.Contains("Newly Mapped Series")),
                "[\"Series\"]"),
            Times.Once);
    }

    [TestMethod]
    public async Task ReevaluatePendingRefreshesAsync_BookNoLongerExists_RemovesTheRow()
    {
        var payload = new PendingRefreshPayload.Snapshot(
            PendingRefreshPayload.CurrentVersion,
            "https://example.com/book",
            "Audible",
            new List<string> { "Author A" },
            new List<string>(),
            "Book Six",
            null, null, null, null,
            new List<string>(),
            null, null, null, null, null, null);

        _pendingRepository.Setup(r => r.GetPendingAudiobookIdsAsync()).ReturnsAsync(new List<long> { 402 });
        _pendingRepository.Setup(r => r.GetByAudiobookIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(new List<PendingMetadataRefresh> { PendingRow(402, payload, "[\"BookName\"]") });
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Database.Models.Audiobook>());

        var result = await CreateService().ReevaluatePendingRefreshesAsync();

        Assert.AreEqual(1, result.Processed);
        Assert.AreEqual(0, result.Updated);
        Assert.AreEqual(1, result.Removed);
        _pendingRepository.Verify(r => r.DeleteByAudiobookIdAsync(402), Times.Once);
    }

    [TestMethod]
    public async Task ReevaluatePendingRefreshesAsync_NoPendingRows_ReturnsZeroes()
    {
        _pendingRepository.Setup(r => r.GetPendingAudiobookIdsAsync()).ReturnsAsync(new List<long>());

        var result = await CreateService().ReevaluatePendingRefreshesAsync();

        Assert.AreEqual(0, result.Processed);
        Assert.AreEqual(0, result.Updated);
        Assert.AreEqual(0, result.Removed);
        _audiobookRepository.Verify(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
    }

    #endregion
}