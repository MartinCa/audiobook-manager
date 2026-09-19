using Microsoft.AspNetCore.Http;
using AudiobookManager.Api;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SeriesControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IOrganize> _clientProxy = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<ISeriesService> _seriesService = null!;
    private AudiobookSaveGate _saveGate = null!;
    private Mock<ILibraryConsistencyService> _libraryConsistencyService = null!;
    private Mock<IUpcomingReleaseService> _upcomingReleaseService = null!;
    private Mock<ILogger<SeriesController>> _logger = null!;
    private SeriesController _controller = null!;

    // Regression: the series name used to be a path segment. A series value is a raw m4b tag and
    // can contain a "/", and ASP.NET Core leaves %2F encoded rather than decoding it into a
    // segment separator - so a series named "Sword Art Online / Progressive" was listed on the
    // overview page and then 404'd the moment it was opened, with no way to match, refresh or
    // ignore anything in it. Verified live against the running API: GET
    // /api/series/Sword%20Art%20Online%20%2F%20Progressive returned 404 while the same name in
    // the query string returns the series.
    //
    // Routing itself cannot be exercised without hosting the app, so this pins the rule that
    // makes it work: no route on either controller may address a series by its name in the path.
    [TestMethod]
    public void SeriesRouteTemplates_NeverPutTheFreeTextSeriesNameInThePath()
    {
        var offenders = new List<string>();

        foreach (var controllerType in new[] { typeof(SeriesController), typeof(BrowseController) })
        {
            foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var route in method.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>())
                {
                    if (route.Template is not null &&
                        route.Template.Contains("{seriesName", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{controllerType.Name}.{method.Name} -> {route.Template}");
                    }
                }
            }
        }

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            offenders.ToArray(),
            $"Series name must travel in the query string: {string.Join(", ", offenders)}");
    }

    [TestInitialize]
    public void Setup()
    {
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _clientProxy = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _seriesService = new Mock<ISeriesService>();
        _saveGate = new AudiobookSaveGate();
        _libraryConsistencyService = new Mock<ILibraryConsistencyService>();
        _upcomingReleaseService = new Mock<IUpcomingReleaseService>();
        _logger = new Mock<ILogger<SeriesController>>();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ISeriesService))).Returns(_seriesService.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService))).Returns(_libraryConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _controller = new SeriesController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            _seriesService.Object,
            _saveGate,
            _libraryConsistencyService.Object,
            _upcomingReleaseService.Object,
            Mock.Of<IHostApplicationLifetime>(),
            _logger.Object);
    }

    // BackgroundOperationRunner calls statusRegistry.SetFinished(key) and THEN releases the
    // static gate in its finally block, so waiting for SetFinished alone can race the gate
    // release (especially since Moq callbacks and TaskCompletionSource can resume our
    // continuation synchronously, inline with the SetFinished call, before the runner's very
    // next statement executes). RunContinuationsAsynchronously keeps that resumption off the
    // runner's thread; AwaitOperationFinished then waits on the gate itself.
    private Task RegisterFinishedWaiter(string operationKey)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry.Setup(r => r.SetFinished(operationKey)).Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private static async Task AwaitOperationFinished(Task finishedSignal)
    {
        await finishedSignal.WaitAsync(TimeSpan.FromSeconds(5));
        await OperationGate.WaitUntilReleasedAsync(typeof(SeriesController));
    }

    private static SeriesOverview MakeOverview(string name = "Mistborn") => new SeriesOverview
    {
        Id = 1,
        Name = name,
        Authors = new List<string> { "Brandon Sanderson" },
        OwnedBookCount = 3,
        IsMatched = true,
        MatchedSourceName = "Hardcover",
        MatchedSourceId = "42",
        ExpectedBookCount = 5,
        MissingBookCount = 2,
        IgnoredBookCount = 0,
        IncludeOmnibusEditions = false
    };

    [TestMethod]
    public async Task GetSeries_ReturnsOnePageWithItemsAndTotal()
    {
        _seriesService
            .Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview> { MakeOverview() }, TotalCount = 211 });

        var result = await _controller.GetSeries();

        var page = ((OkObjectResult)result.Result!).Value as SeriesOverviewPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(211, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Mistborn", page.Items[0].Name);
        Assert.AreEqual(3, page.Items[0].OwnedBookCount);
        Assert.IsTrue(page.Items[0].IsMatched);
    }

    [TestMethod]
    public async Task GetSeries_PassesPagePageSizeSearchAndMatchedThrough()
    {
        _seriesService
            .Setup(s => s.GetSeriesOverviewPageAsync(4, 25, "mist", false))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 3 });

        var result = await _controller.GetSeries(page: 4, pageSize: 25, search: "mist", matched: false);

        var page = ((OkObjectResult)result.Result!).Value as SeriesOverviewPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(3, page.TotalCount);
        _seriesService.Verify(s => s.GetSeriesOverviewPageAsync(4, 25, "mist", false), Times.Once);
    }

    [TestMethod]
    [DataRow(-1, 50)]
    [DataRow(0, 0)]
    [DataRow(0, 201)]
    public async Task GetSeries_AnOutOfRangePage_IsRefused(int page, int pageSize)
    {
        var result = await _controller.GetSeries(page: page, pageSize: pageSize);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(
            s => s.GetSeriesOverviewPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);
    }

    // Regression mirroring UrlCleanupControllerTests: the offset is widened before multiplying so
    // a huge page cannot wrap negative and silently serve the first page.
    [TestMethod]
    public async Task GetSeries_APageLargeEnoughToOverflowTheOffset_IsRefused()
    {
        var result = await _controller.GetSeries(page: 11_000_000, pageSize: 200);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(
            s => s.GetSeriesOverviewPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetSeriesCounts_MapsCountsToDto()
    {
        _seriesService.Setup(s => s.GetSeriesOverviewCountsAsync())
            .ReturnsAsync(new SeriesOverviewCounts { Total = 42, Matched = 18, Unmatched = 24 });

        var result = await _controller.GetSeriesCounts();

        Assert.AreEqual(42, result.Total);
        Assert.AreEqual(18, result.Matched);
        Assert.AreEqual(24, result.Unmatched);
    }

    [TestMethod]
    public async Task GetSeriesDetail_Found_ReturnsMappedMagesWithTotals()
    {
        _seriesService
            .Setup(s => s.GetSeriesDetailPageAsync(
                "Mistborn", ownedSkip: 0, ownedTake: 50, missingSkip: 0, missingTake: 50, ignoredSkip: 0, ignoredTake: 50, partMismatchSkip: 0, partMismatchTake: 50))
            .ReturnsAsync(new SeriesDetailPage
            {
                Overview = MakeOverview(),
                OwnedBooks = new List<SeriesOwnedBook>
                {
                    new SeriesOwnedBook { Id = 1, BookName = "The Final Empire", Year = 2006, Authors = new List<string> { "Brandon Sanderson" }, Narrators = new List<string>() }
                },
                OwnedBookTotal = 3,
                MissingBooks = new List<SeriesExpectedBookInfo>
                {
                    new SeriesExpectedBookInfo { Id = 10, Title = "Missing Book", Position = "4" }
                },
                MissingBookTotal = 7,
                IgnoredBooks = new List<SeriesExpectedBookInfo>(),
                IgnoredBookTotal = 2,
                PartMismatches = new List<SeriesPartMismatch>
                {
                    new SeriesPartMismatch { AudiobookId = 1, BookName = "The Final Empire", StoredPart = "7", ExpectedPart = "1", RosterTitle = "The Final Empire" }
                },
                PartMismatchTotal = 5
            });

        var result = await _controller.GetSeriesDetail("Mistborn");

        Assert.IsNotNull(result.Value);
        var dto = result.Value!;
        Assert.AreEqual("Mistborn", dto.Overview.Name);
        Assert.AreEqual(1, dto.OwnedBooks.Items.Count);
        Assert.AreEqual(3, dto.OwnedBooks.TotalCount);
        Assert.AreEqual(1, dto.MissingBooks.Items.Count);
        Assert.AreEqual(7, dto.MissingBooks.TotalCount);
        Assert.AreEqual(0, dto.IgnoredBooks.Items.Count);
        Assert.AreEqual(2, dto.IgnoredBooks.TotalCount);
        Assert.AreEqual("Missing Book", dto.MissingBooks.Items[0].Title);
        Assert.AreEqual(1, dto.PartMismatches.Items.Count);
        Assert.AreEqual(5, dto.PartMismatches.TotalCount, "the total is the full mismatch count, not the page");
        var mismatch = dto.PartMismatches.Items[0];
        Assert.AreEqual(1, mismatch.AudiobookId);
        Assert.AreEqual("7", mismatch.StoredPart);
        Assert.AreEqual("1", mismatch.ExpectedPart);
        Assert.AreEqual("The Final Empire", mismatch.RosterTitle);
    }

    // Only the section's own page parameters are sliced from the reconciliation; an out-of-range
    // part-mismatch page must be refused like any other section's, and nothing is fetched.
    [TestMethod]
    public async Task GetSeriesDetail_AnOutOfRangePartMismatchPage_IsRefused()
    {
        var result = await _controller.GetSeriesDetail("Mistborn", partMismatchPage: -1);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(
            s => s.GetSeriesDetailPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    // Regression: the owned section used to serve a minimal DTO with no coverFilePath, so the
    // series view's rows always rendered the placeholder. Library and author views already carried
    // it via AudiobookSummaryDto; the owned DTO must too.
    [TestMethod]
    public async Task GetSeriesDetail_OwnedBookDtoCarriesTheCoverFilePath()
    {
        const string coverPath = "/library/Brandon Sanderson/Mistborn/2006 - The Final Empire/cover.jpg";
        _seriesService
            .Setup(s => s.GetSeriesDetailPageAsync(
                "Mistborn", ownedSkip: 0, ownedTake: 50, missingSkip: 0, missingTake: 50, ignoredSkip: 0, ignoredTake: 50, partMismatchSkip: 0, partMismatchTake: 50))
            .ReturnsAsync(new SeriesDetailPage
            {
                Overview = MakeOverview(),
                OwnedBooks = new List<SeriesOwnedBook>
                {
                    new SeriesOwnedBook { Id = 1, BookName = "The Final Empire", Year = 2006, Authors = new List<string> { "Brandon Sanderson" }, Narrators = new List<string>(), CoverFilePath = coverPath }
                },
                OwnedBookTotal = 3,
                MissingBooks = new List<SeriesExpectedBookInfo>(),
                MissingBookTotal = 0,
                IgnoredBooks = new List<SeriesExpectedBookInfo>(),
                IgnoredBookTotal = 0
            });

        var result = await _controller.GetSeriesDetail("Mistborn");

        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, result.Value!.OwnedBooks.Items.Count);
        Assert.AreEqual(coverPath, result.Value.OwnedBooks.Items[0].CoverFilePath);
    }

    [TestMethod]
    public async Task GetSeriesDetail_NotFound_Returns404()
    {
        _seriesService
            .Setup(s => s.GetSeriesDetailPageAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((SeriesDetailPage?)null);

        var result = await _controller.GetSeriesDetail("Unknown");

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task GetSeriesDetail_AnOutOfRangeSectionPage_IsRefused()
    {
        var result = await _controller.GetSeriesDetail("Mistborn", missingPage: -1);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(
            s => s.GetSeriesDetailPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetMatchCandidates_Success_ReturnsMappedList()
    {
        _seriesService.Setup(s => s.SuggestSeriesMatchesAsync("Mistborn")).ReturnsAsync(new List<SeriesMatchCandidate>
        {
            new SeriesMatchCandidate { SourceName = "Hardcover", SourceId = "42", SeriesName = "Mistborn", Confidence = 0.9 }
        });

        var result = await _controller.GetMatchCandidates("Mistborn");

        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, result.Value!.Count);
        Assert.AreEqual("Hardcover", result.Value[0].SourceName);
    }

    [TestMethod]
    public async Task GetMatchCandidates_ServiceThrows_Returns500()
    {
        _seriesService.Setup(s => s.SuggestSeriesMatchesAsync("Mistborn")).ThrowsAsync(new Exception("boom"));

        var result = await _controller.GetMatchCandidates("Mistborn");

        ProblemAssert.HasDetail(
            result.Result, StatusCodes.Status500InternalServerError, ProblemResults.UnexpectedErrorDetail);
    }

    // The exception messages these replaced carried absolute container paths and .NET type detail
    // straight to the caller. The message is still logged; it is only the response that is stable.
    [TestMethod]
    public async Task GetMatchCandidates_ServiceThrows_DoesNotReturnTheExceptionMessage()
    {
        _seriesService.Setup(s => s.SuggestSeriesMatchesAsync("Mistborn"))
            .ThrowsAsync(new Exception("/config/secrets/appsettings.json could not be read"));

        var result = await _controller.GetMatchCandidates("Mistborn");

        var problem = ProblemAssert.HasStatus(result.Result, StatusCodes.Status500InternalServerError);
        StringAssert.DoesNotMatch(problem.Detail!, new System.Text.RegularExpressions.Regex("/config/secrets"));
    }

    [TestMethod]
    public async Task SearchMatchCandidates_BlankQuery_ReturnsBadRequest()
    {
        var result = await _controller.SearchMatchCandidates("Mistborn", "  ");

        ProblemAssert.HasStatus(result.Result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task SearchMatchCandidates_Success_ReturnsMappedList()
    {
        _seriesService.Setup(s => s.SearchSeriesMatchesAsync("Mistborn", "mist"))
            .ReturnsAsync(new List<SeriesMatchCandidate> { new SeriesMatchCandidate { SourceName = "Audible", SourceId = "1", SeriesName = "Mistborn", Confidence = 0.5 } });

        var result = await _controller.SearchMatchCandidates("Mistborn", "mist");

        Assert.AreEqual(1, result.Value!.Count);
        Assert.AreEqual("Audible", result.Value[0].SourceName);
    }

    [TestMethod]
    public async Task MatchSeries_MissingSourceFields_ReturnsBadRequest()
    {
        var result = await _controller.MatchSeries("Mistborn", new MatchSeriesDto { SourceName = "", SourceId = "" });

        ProblemAssert.HasStatus(result.Result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task MatchSeries_Success_ReturnsMappedOverview()
    {
        _seriesService.Setup(s => s.MatchSeriesAsync("Mistborn", "Hardcover", "42", null, false))
            .ReturnsAsync(MakeOverview());

        var result = await _controller.MatchSeries("Mistborn", new MatchSeriesDto { SourceName = "Hardcover", SourceId = "42" });

        Assert.IsNotNull(result.Value);
        Assert.AreEqual("Mistborn", result.Value!.Name);
    }

    [TestMethod]
    public async Task MatchSeries_ArgumentException_ReturnsBadRequest()
    {
        _seriesService.Setup(s => s.MatchSeriesAsync("Mistborn", "Hardcover", "42", null, false))
            .ThrowsAsync(new ArgumentException("bad series id"));

        var result = await _controller.MatchSeries("Mistborn", new MatchSeriesDto { SourceName = "Hardcover", SourceId = "42" });

        ProblemAssert.HasStatus(result.Result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task MatchSeries_UnexpectedException_Returns500()
    {
        _seriesService.Setup(s => s.MatchSeriesAsync("Mistborn", "Hardcover", "42", null, false))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _controller.MatchSeries("Mistborn", new MatchSeriesDto { SourceName = "Hardcover", SourceId = "42" });

        var statusResult = (ObjectResult)result.Result!;
        Assert.AreEqual(500, statusResult.StatusCode);
    }

    [TestMethod]
    public async Task SetIncludeOmnibusEditions_Success_ReturnsMappedOverview()
    {
        _seriesService.Setup(s => s.SetIncludeOmnibusEditionsAsync("Mistborn", true))
            .ReturnsAsync(MakeOverview());

        var result = await _controller.SetIncludeOmnibusEditions("Mistborn", new IncludeOmnibusEditionsDto { IncludeOmnibusEditions = true });

        Assert.IsNotNull(result.Value);
        Assert.AreEqual("Mistborn", result.Value!.Name);
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_NoPositionOrTitle_ReturnsBadRequest()
    {
        var result = await _controller.IgnoreExpectedBook("Mistborn", new ExpectedBookRefDto());

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_Success_ReturnsOk()
    {
        var result = await _controller.IgnoreExpectedBook("Mistborn", new ExpectedBookRefDto { Position = "1", Title = "Book" });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _seriesService.Verify(s => s.IgnoreExpectedBookAsync("Mistborn", "1", "Book", true), Times.Once);
    }

    [TestMethod]
    public async Task UnignoreExpectedBook_Success_ReturnsOk()
    {
        var result = await _controller.UnignoreExpectedBook("Mistborn", new ExpectedBookRefDto { Position = "1", Title = "Book" });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _seriesService.Verify(s => s.IgnoreExpectedBookAsync("Mistborn", "1", "Book", false), Times.Once);
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_NotFound_Returns404()
    {
        _seriesService.Setup(s => s.IgnoreExpectedBookAsync("Mistborn", "1", "Book", true))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.IgnoreExpectedBook("Mistborn", new ExpectedBookRefDto { Position = "1", Title = "Book" });

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_UnexpectedException_Returns500()
    {
        _seriesService.Setup(s => s.IgnoreExpectedBookAsync("Mistborn", "1", "Book", true))
            .ThrowsAsync(new Exception("boom"));

        var result = await _controller.IgnoreExpectedBook("Mistborn", new ExpectedBookRefDto { Position = "1", Title = "Book" });

        var statusResult = (ObjectResult)result;
        Assert.AreEqual(500, statusResult.StatusCode);
    }

    [TestMethod]
    public async Task StartBulkMatch_InvalidThreshold_ReturnsBadRequest()
    {
        var result = _controller.StartBulkMatch(new BulkMatchSeriesDto { ConfidenceThreshold = 1.5 });

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task StartBulkMatch_ReturnsOkImmediately_AndWiresProgressAndCompletion()
    {
        _seriesService.Setup(s => s.BulkAutoMatchSeriesAsync(0.85, null, It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((double _, List<string>? __, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 1, 1, 0).GetAwaiter().GetResult();
                return (1, 1, 0, (string?)null);
            });

        var finished = RegisterFinishedWaiter(SeriesController.MatchOperationKey);

        var result = _controller.StartBulkMatch(new BulkMatchSeriesDto());

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesMatchProgress(It.Is<SeriesMatchProgress>(p => p.Processed == 1 && p.Total == 1)), Times.Once);
        _clientProxy.Verify(c => c.SeriesMatchComplete(It.Is<SeriesMatchComplete>(p => p.TotalSucceeded == 1 && p.TotalFailed == 0)), Times.Once);
    }

    [TestMethod]
    public async Task RefreshSeries_Success_ReturnsMappedResult()
    {
        _seriesService.Setup(s => s.RefreshSeriesAsync("Mistborn"))
            .ReturnsAsync(new SeriesRefreshResult(Success: true, HasChanges: true, ChangeCount: 3, SourceName: "Hardcover"));

        var result = await _controller.RefreshSeries("Mistborn");

        var dto = ((OkObjectResult)result.Result!).Value as SeriesRefreshResultDto;
        Assert.IsNotNull(dto);
        Assert.IsTrue(dto.Success);
        Assert.IsTrue(dto.HasChanges);
        Assert.AreEqual(3, dto.ChangeCount);
        Assert.AreEqual("Hardcover", dto.SourceName);
    }

    [TestMethod]
    public async Task RefreshSeries_NoChanges_ReportsSuccessWithNoChanges()
    {
        _seriesService.Setup(s => s.RefreshSeriesAsync("Mistborn"))
            .ReturnsAsync(new SeriesRefreshResult(Success: true, HasChanges: false, ChangeCount: 0, SourceName: "Hardcover"));

        var result = await _controller.RefreshSeries("Mistborn");

        var dto = ((OkObjectResult)result.Result!).Value as SeriesRefreshResultDto;
        Assert.IsNotNull(dto);
        Assert.IsTrue(dto.Success);
        Assert.IsFalse(dto.HasChanges);
        Assert.AreEqual(0, dto.ChangeCount);
    }

    [TestMethod]
    public async Task RefreshSeries_UnmatchedOrUnknown_ReturnsNotFound()
    {
        _seriesService.Setup(s => s.RefreshSeriesAsync("Mistborn")).ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.RefreshSeries("Mistborn");

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    // Review finding 2: the synchronous single refresh shares the bulk refresh's gate, so a
    // sweep (or a pending apply) that is already running refuses a second refresh with the same
    // 409 the fire-and-forget endpoints use, instead of letting both mutate the roster in
    // parallel (or parking the request thread on the lock).
    [TestMethod]
    public async Task RefreshSeries_RefreshAlreadyRunning_ReturnsConflict()
    {
        var refreshLock = (SemaphoreSlim)typeof(SeriesController)
            .GetField("_refreshLock", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        Assert.IsTrue(refreshLock.Wait(0));

        try
        {
            var result = await _controller.RefreshSeries("Mistborn");

            ProblemAssert.HasDetail(result.Result, StatusCodes.Status409Conflict, "A series refresh or pending apply is already in progress.");
            _seriesService.Verify(s => s.RefreshSeriesAsync(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>
    /// The dismiss takes the same gate the refresh and the pending apply hold, for the same reason
    /// they hold it against each other: all three read and then replace the pending row. Without
    /// it a dismiss landing between an apply's recompute and its upsert deleted a row the apply
    /// then wrote straight back, so the snapshot the user dismissed reappeared.
    /// </summary>
    [TestMethod]
    public async Task DismissPending_RefreshOrApplyAlreadyRunning_ReturnsConflict()
    {
        var refreshLock = (SemaphoreSlim)typeof(SeriesController)
            .GetField("_refreshLock", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        Assert.IsTrue(refreshLock.Wait(0));

        try
        {
            var result = await _controller.DismissPending("Mistborn");

            ProblemAssert.HasDetail(result, StatusCodes.Status409Conflict, "A series refresh or pending apply is already in progress.");
            _seriesService.Verify(s => s.DismissPendingSeriesRefreshAsync(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>The gate is released again, so a dismiss does not wedge every later refresh.</summary>
    [TestMethod]
    public async Task DismissPending_ReleasesTheRefreshGate()
    {
        _seriesService.Setup(s => s.DismissPendingSeriesRefreshAsync("Mistborn")).ReturnsAsync(true);

        await _controller.DismissPending("Mistborn");

        var refreshLock = (SemaphoreSlim)typeof(SeriesController)
            .GetField("_refreshLock", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        Assert.IsTrue(refreshLock.Wait(0), "the dismiss must not leave the refresh gate held");
        refreshLock.Release();
    }

    [TestMethod]
    public async Task RefreshSeries_UnexpectedException_Returns500()
    {
        _seriesService.Setup(s => s.RefreshSeriesAsync("Mistborn")).ThrowsAsync(new Exception("boom"));

        var result = await _controller.RefreshSeries("Mistborn");

        Assert.AreEqual(StatusCodes.Status500InternalServerError, ((ObjectResult)result.Result!).StatusCode);
    }

    [TestMethod]
    public async Task RefreshSeries_NoSeriesCapableScraper_ReturnsBadRequest()
    {
        // A series matched to a source this build has no series-capable scraper for is a
        // caller-fixable condition, not server state: the message is relayed as a 4xx.
        _seriesService.Setup(s => s.RefreshSeriesAsync("Mistborn"))
            .ThrowsAsync(new ArgumentException("No series-capable scraper for source Hardcover"));

        var result = await _controller.RefreshSeries("Mistborn");

        ProblemAssert.HasDetail(result.Result, StatusCodes.Status400BadRequest, "No series-capable scraper for source Hardcover");
    }

    [TestMethod]
    public async Task StartRefreshAllSeries_ReturnsOkImmediately_AndWiresCompletion()
    {
        _seriesService.Setup(s => s.RefreshAllSeriesAsync(It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((3, 3, 0, (string?)null));

        var finished = RegisterFinishedWaiter(SeriesController.RefreshOperationKey);

        var result = _controller.StartRefreshAllSeries();

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesRefreshComplete(It.Is<SeriesRefreshComplete>(p => p.TotalSucceeded == 3)), Times.Once);
    }

    [TestMethod]
    public async Task StartRefreshAllSeries_AlreadyRunning_ReturnsConflict()
    {
        var release = new TaskCompletionSource();
        _seriesService.Setup(s => s.RefreshAllSeriesAsync(It.IsAny<Func<int, int, int, int, Task>>()))
            .Returns(async (Func<int, int, int, int, Task> _) =>
            {
                await release.Task;
                return (1, 1, 0, (string?)null);
            });

        var first = _controller.StartRefreshAllSeries();
        Assert.IsInstanceOfType(first, typeof(OkResult));

        var second = _controller.StartRefreshAllSeries();
        ProblemAssert.HasStatus(second, StatusCodes.Status409Conflict);

        var finished = RegisterFinishedWaiter(SeriesController.RefreshOperationKey);
        release.SetResult();
        await AwaitOperationFinished(finished);
    }

    [TestMethod]
    public void StartDeleteSeries_BlankSeriesName_ReturnsBadRequest()
    {
        var result = _controller.StartDeleteSeries(" ");

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
        _seriesService.Verify(
            s => s.DeleteSeriesAsync(It.IsAny<string>(), It.IsAny<Func<int, int, int, int, Task>>()), Times.Never);
    }

    [TestMethod]
    public async Task StartDeleteSeries_ReturnsOkImmediately_AndWiresProgressAndCompletion()
    {
        _seriesService.Setup(s => s.DeleteSeriesAsync("Mistborn", It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((string _, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 1, 1, 0).GetAwaiter().GetResult();
                return (1, 1, 0);
            });

        var finished = RegisterFinishedWaiter(SeriesController.DeleteOperationKey);

        var result = _controller.StartDeleteSeries("Mistborn");

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesDeleteProgress(It.Is<SeriesDeleteProgress>(p => p.Processed == 1 && p.Total == 1)), Times.Once);
        _clientProxy.Verify(c => c.SeriesDeleteComplete(It.Is<SeriesDeleteComplete>(p => p.TotalSucceeded == 1 && p.TotalFailed == 0 && !p.Errored)), Times.Once);
    }

    /// <summary>
    /// A service failure (e.g. the catalog row delete itself throwing) escapes the background
    /// work delegate, so BackgroundOperationRunner's error path sends the completion event
    /// instead of the normal one. Every count on that path is zero - the same shape as a genuine
    /// "series with no owned books" success - so Errored is what a client needs to tell the two
    /// apart instead of toasting a crashed delete as a success.
    /// </summary>
    [TestMethod]
    public async Task StartDeleteSeries_ServiceThrows_SendsCompletionWithErroredTrue()
    {
        _seriesService.Setup(s => s.DeleteSeriesAsync("Mistborn", It.IsAny<Func<int, int, int, int, Task>>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var finished = RegisterFinishedWaiter(SeriesController.DeleteOperationKey);

        var result = _controller.StartDeleteSeries("Mistborn");

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(
            c => c.SeriesDeleteComplete(It.Is<SeriesDeleteComplete>(
                p => p.TotalProcessed == 0 && p.TotalSucceeded == 0 && p.TotalFailed == 0 && p.Errored)),
            Times.Once);
    }

    [TestMethod]
    public async Task StartDeleteSeries_AlreadyRunning_ReturnsConflict()
    {
        var release = new TaskCompletionSource();
        _seriesService.Setup(s => s.DeleteSeriesAsync("Mistborn", It.IsAny<Func<int, int, int, int, Task>>()))
            .Returns(async (string _, Func<int, int, int, int, Task> _) =>
            {
                await release.Task;
                return (1, 1, 0);
            });

        var first = _controller.StartDeleteSeries("Mistborn");
        Assert.IsInstanceOfType(first, typeof(OkResult));

        var second = _controller.StartDeleteSeries("Mistborn");
        ProblemAssert.HasStatus(second, StatusCodes.Status409Conflict);

        var finished = RegisterFinishedWaiter(SeriesController.DeleteOperationKey);
        release.SetResult();
        await AwaitOperationFinished(finished);
    }

    private static AudiobookManager.Domain.PendingSeriesRefresh MakePendingRefresh() =>
        new(
            "Mistborn",
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            "Hardcover",
            "https://hardcover.app/series/42",
            "Mistborn Saga",
            new List<SeriesRefreshChange>
            {
                new(SeriesRefreshChangeType.PartUpdate, 5, "Book A", "01", "02", "Book A", null, null, null),
                new(SeriesRefreshChangeType.MissingBook, null, null, null, null, null, "4", "Book B", 2010),
                new(SeriesRefreshChangeType.PartRemoval, 7, "Book C", "3", null, "Book C", null, null, null),
            },
            new List<SeriesRefreshRosterEntry>
            {
                new("1", "Book A", 2006, null, false),
                new("4", "Book B", 2010, null, false),
            });

    [TestMethod]
    public async Task GetPendingPage_Success_ReturnsMappedPage()
    {
        _seriesService.Setup(s => s.GetPendingSeriesRefreshPageAsync(0, 50))
            .ReturnsAsync((
                new List<PendingSeriesRefreshListItem>
                {
                    new("Mistborn", "Hardcover", "Mistborn Saga", new DateTime(2026, 9, 1), 3),
                },
                7));

        var result = await _controller.GetPendingPage();

        var page = ((OkObjectResult)result.Result!).Value as SeriesRefreshPendingPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(7, page.Total);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Mistborn", page.Items[0].SeriesName);
        Assert.AreEqual(3, page.Items[0].ChangeCount);
        Assert.AreEqual("Mistborn Saga", page.Items[0].SourceSeriesName);
    }

    [TestMethod]
    [DataRow(-1, 50)]
    [DataRow(0, 0)]
    [DataRow(0, 201)]
    public async Task GetPendingPage_AnOutOfRangePage_IsRefused(int page, int pageSize)
    {
        var result = await _controller.GetPendingPage(page: page, pageSize: pageSize);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(s => s.GetPendingSeriesRefreshPageAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task GetPendingCount_ReturnsCount()
    {
        _seriesService.Setup(s => s.CountPendingSeriesRefreshesAsync()).ReturnsAsync(12);

        var result = await _controller.GetPendingCount();

        Assert.AreEqual(12, ((OkObjectResult)result.Result!).Value);
    }

    [TestMethod]
    public async Task GetPendingDetail_NoPendingSnapshot_Returns204()
    {
        // 204 rather than 404: absence is the normal state the series page polls on every
        // visit, and the browser logs every non-2xx response as a console error no client-side
        // handling can silence.
        _seriesService.Setup(s => s.GetPendingSeriesRefreshAsync("Mistborn")).ReturnsAsync((PendingSeriesRefresh?)null);

        var result = await _controller.GetPendingDetail("Mistborn");

        Assert.IsInstanceOfType(result.Result, typeof(NoContentResult));
    }

    [TestMethod]
    public async Task GetPendingDetail_Success_ReturnsMappedDto()
    {
        _seriesService.Setup(s => s.GetPendingSeriesRefreshAsync("Mistborn")).ReturnsAsync(MakePendingRefresh());

        var result = await _controller.GetPendingDetail("Mistborn");

        var dto = ((OkObjectResult)result.Result!).Value as SeriesRefreshPendingDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual("Hardcover", dto.SourceName);
        Assert.AreEqual("Mistborn Saga", dto.SourceSeriesName);
        Assert.AreEqual(3, dto.Changes.Count);
        Assert.AreEqual("PartUpdate", dto.Changes[0].ChangeType);
        Assert.AreEqual("02", dto.Changes[0].NewPart);
        Assert.AreEqual("MissingBook", dto.Changes[1].ChangeType);
        Assert.AreEqual("Book B", dto.Changes[1].Title);
        Assert.AreEqual("PartRemoval", dto.Changes[2].ChangeType);
        Assert.AreEqual(7, dto.Changes[2].AudiobookId);
    }

    [TestMethod]
    public async Task GetPendingDetail_ServiceThrows_Returns500()
    {
        _seriesService.Setup(s => s.GetPendingSeriesRefreshAsync("Mistborn"))
            .ThrowsAsync(new Exception("boom"));

        var result = await _controller.GetPendingDetail("Mistborn");

        ProblemAssert.HasDetail(
            result.Result, StatusCodes.Status500InternalServerError, ProblemResults.UnexpectedErrorDetail);
    }

    [TestMethod]
    public async Task DismissPending_ReturnsOk()
    {
        _seriesService.Setup(s => s.DismissPendingSeriesRefreshAsync("Mistborn")).ReturnsAsync(true);

        var result = await _controller.DismissPending("Mistborn");

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _seriesService.Verify(s => s.DismissPendingSeriesRefreshAsync("Mistborn"), Times.Once);
    }

    [TestMethod]
    public async Task StartPendingApply_EmptyRequest_ReturnsBadRequest()
    {
        var result = _controller.StartPendingApply("Mistborn", new ApplySeriesRefreshRequestDto());

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "At least one accepted change, or the source-series-name adoption, is required.");
    }

    [TestMethod]
    public async Task StartPendingApply_UnknownChangeType_ReturnsBadRequest()
    {
        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections = { new ApplySeriesRefreshChangeDto { ChangeType = "Bogus", AudiobookId = 5 } },
        };

        var result = _controller.StartPendingApply("Mistborn", dto);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Unknown ChangeType 'Bogus'.");
    }

    [TestMethod]
    public async Task StartPendingApply_DuplicateAudiobookId_ReturnsBadRequest()
    {
        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections =
            {
                new ApplySeriesRefreshChangeDto { ChangeType = "PartUpdate", AudiobookId = 5 },
                new ApplySeriesRefreshChangeDto { ChangeType = "PartRemoval", AudiobookId = 5 },
            },
        };

        var result = _controller.StartPendingApply("Mistborn", dto);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "A library book can only be changed once: audiobook 5 appears more than once in the request.");
    }

    // Review finding 4: two MissingBook selections may not target the same roster entry - even
    // when their natural keys differ only by case/whitespace, which the apply would resolve to
    // the same row. The duplicate must be rejected before the batch starts.
    [TestMethod]
    public async Task StartPendingApply_DuplicateMissingBookTarget_ReturnsBadRequest()
    {
        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections =
            {
                new ApplySeriesRefreshChangeDto { ChangeType = "MissingBook", AudiobookId = 5, Position = "4", Title = "Book B" },
                new ApplySeriesRefreshChangeDto { ChangeType = "MissingBook", AudiobookId = 6, Position = " 4 ", Title = " book b " },
            },
        };

        var result = _controller.StartPendingApply("Mistborn", dto);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "A missing book can only be applied once: more than one selection targets the same roster entry.");
    }

    [TestMethod]
    public async Task StartPendingApply_MissingBookWithoutANaturalKey_ReturnsBadRequest()
    {
        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections =
            {
                new ApplySeriesRefreshChangeDto { ChangeType = "MissingBook", AudiobookId = 5 },
            },
        };

        var result = _controller.StartPendingApply("Mistborn", dto);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Every MissingBook change needs a Position or Title to identify the roster entry.");
    }

    [TestMethod]
    public async Task StartPendingApply_ReturnsOkImmediately_AndWiresProgressAndCompletion()
    {
        _seriesService
            .Setup(s => s.ApplyPendingSeriesRefreshAsync(
                "Mistborn",
                It.Is<SeriesRefreshApplyRequest>(r =>
                    r.Selections.Count == 1 &&
                    r.Selections[0].Type == SeriesRefreshChangeType.PartUpdate &&
                    r.Selections[0].AudiobookId == 5),
                It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((string _, SeriesRefreshApplyRequest __, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 1, 1, 0).GetAwaiter().GetResult();
                return (1, 1, 0, (string?)null);
            });

        var finished = RegisterFinishedWaiter(SeriesController.PendingApplyOperationKey);

        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections = { new ApplySeriesRefreshChangeDto { ChangeType = "PartUpdate", AudiobookId = 5 } },
        };
        var result = _controller.StartPendingApply("Mistborn", dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesRefreshApplyProgress(It.Is<SeriesRefreshApplyProgress>(p => p.Processed == 1 && p.Total == 1)), Times.Once);
        _clientProxy.Verify(c => c.SeriesRefreshApplyComplete(It.Is<SeriesRefreshApplyComplete>(p => p.SeriesName == "Mistborn" && p.TotalSucceeded == 1 && p.TotalFailed == 0 && p.EffectiveSeriesName == null)), Times.Once);
    }

    // The completion event must tell the client when the apply adopted the source's series name:
    // a renamed series is no longer addressable under the route the user came in on, and the
    // client navigates to the adopted name only if the completion actually reports it.
    [TestMethod]
    public async Task StartPendingApply_ReportsTheAdoptedNameInTheCompletionEvent()
    {
        _seriesService
            .Setup(s => s.ApplyPendingSeriesRefreshAsync(
                "Mistborn",
                It.IsAny<SeriesRefreshApplyRequest>(),
                It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((string _, SeriesRefreshApplyRequest __, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 1, 1, 0).GetAwaiter().GetResult();
                return (1, 1, 0, "Mistborn Saga");
            });

        var finished = RegisterFinishedWaiter(SeriesController.PendingApplyOperationKey);

        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections = { new ApplySeriesRefreshChangeDto { ChangeType = "PartUpdate", AudiobookId = 5 } },
        };
        var result = _controller.StartPendingApply("Mistborn", dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesRefreshApplyComplete(It.Is<SeriesRefreshApplyComplete>(p => p.SeriesName == "Mistborn" && p.EffectiveSeriesName == "Mistborn Saga")), Times.Once);
    }

    /// <summary>
    /// The completion is broadcast to every connection, but it names the series the apply was
    /// requested for - a client reviewing a DIFFERENT series must be able to tell the two apart
    /// (a dialog would otherwise close itself or navigate on someone else's result, e.g. when
    /// an apply started from the metadata-refresh page lands while another series' review is
    /// open). The originating name travels in the payload verbatim, not the adopted/effective
    /// name, because that is the name the applying dialog presented when it started the apply.
    /// </summary>
    [TestMethod]
    public async Task StartPendingApply_CompletionCarriesTheOriginatingSeriesName()
    {
        _seriesService
            .Setup(s => s.ApplyPendingSeriesRefreshAsync(
                "Mistborn",
                It.IsAny<SeriesRefreshApplyRequest>(),
                It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((string _, SeriesRefreshApplyRequest __, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 1, 1, 0).GetAwaiter().GetResult();
                return (1, 1, 0, (string?)null);
            });

        var finished = RegisterFinishedWaiter(SeriesController.PendingApplyOperationKey);

        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections = { new ApplySeriesRefreshChangeDto { ChangeType = "PartUpdate", AudiobookId = 5 } },
        };
        var result = _controller.StartPendingApply("Mistborn", dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesRefreshApplyComplete(It.Is<SeriesRefreshApplyComplete>(p =>
            p.SeriesName == "Mistborn" && p.EffectiveSeriesName == null)), Times.Once);
    }

    /// <summary>
    /// The error path (the apply threw out of the background work) also broadcasts a completion,
    /// and that one must carry the originating series name too, or a client that scopes its
    /// completion handling by series would treat a crashed apply's zeroed counts as an event for
    /// some other series (or, worse, a listening dialog for the right series would miss it).
    /// </summary>
    [TestMethod]
    public async Task StartPendingApply_ErrorPathCompletionCarriesTheOriginatingSeriesName()
    {
        _seriesService
            .Setup(s => s.ApplyPendingSeriesRefreshAsync(
                "Mistborn",
                It.IsAny<SeriesRefreshApplyRequest>(),
                It.IsAny<Func<int, int, int, int, Task>>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var finished = RegisterFinishedWaiter(SeriesController.PendingApplyOperationKey);

        var dto = new ApplySeriesRefreshRequestDto
        {
            Selections = { new ApplySeriesRefreshChangeDto { ChangeType = "PartUpdate", AudiobookId = 5 } },
        };
        var result = _controller.StartPendingApply("Mistborn", dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesRefreshApplyComplete(It.Is<SeriesRefreshApplyComplete>(p =>
            p.TotalProcessed == 0 && p.TotalSucceeded == 0 && p.TotalFailed == 0 && p.SeriesName == "Mistborn" && p.EffectiveSeriesName == null)), Times.Once);
    }

    [TestMethod]
    public async Task GetMissingBookCandidates_NoPositionOrTitle_ReturnsBadRequest()
    {
        var result = await _controller.GetMissingBookCandidates("Mistborn", " ", null);

        ProblemAssert.HasDetail(
            result.Result, StatusCodes.Status400BadRequest, "Position or Title is required to identify the expected book.");
    }

    [TestMethod]
    public async Task GetMissingBookCandidates_Success_ReturnsMappedList()
    {
        _seriesService.Setup(s => s.FindMissingBookCandidatesAsync("Mistborn", "3", "The Hero of Ages"))
            .ReturnsAsync(new List<SeriesBookCandidate>
            {
                new()
                {
                    AudiobookId = 5, BookName = "Hero of Ages", Series = null, SeriesPart = null, Year = 2010,
                    Authors = new List<string> { "Brandon Sanderson" }, TitleSimilarity = 0.75, AuthorMatches = true,
                },
            });

        var result = await _controller.GetMissingBookCandidates("Mistborn", "3", "The Hero of Ages");

        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, result.Value!.Count);
        var dto = result.Value[0];
        Assert.AreEqual(5, dto.AudiobookId);
        Assert.AreEqual("Hero of Ages", dto.BookName);
        Assert.IsNull(dto.Series);
        Assert.IsNull(dto.SeriesPart);
        Assert.AreEqual(2010, dto.Year);
        CollectionAssert.AreEqual(new List<string> { "Brandon Sanderson" }, dto.Authors);
        Assert.AreEqual(0.75, dto.TitleSimilarity);
        Assert.IsTrue(dto.AuthorMatches);
    }

    [TestMethod]
    public async Task GetMissingBookCandidates_UnknownExpectedBook_Returns404()
    {
        _seriesService.Setup(s => s.FindMissingBookCandidatesAsync("Mistborn", "9", "Nope"))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.GetMissingBookCandidates("Mistborn", "9", "Nope");

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task GetMissingBookCandidates_ServiceThrows_Returns500()
    {
        _seriesService.Setup(s => s.FindMissingBookCandidatesAsync("Mistborn", "3", "The Hero of Ages"))
            .ThrowsAsync(new Exception("boom"));

        var result = await _controller.GetMissingBookCandidates("Mistborn", "3", "The Hero of Ages");

        ProblemAssert.HasDetail(
            result.Result, StatusCodes.Status500InternalServerError, ProblemResults.UnexpectedErrorDetail);
    }

    [TestMethod]
    public async Task ApplyExpectedBook_MissingAudiobookId_ReturnsBadRequest()
    {
        var result = await _controller.ApplyExpectedBook("Mistborn", new ApplyExpectedBookDto { AudiobookId = 0, Position = "3" });

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ApplyExpectedBook_NoPositionOrTitle_ReturnsBadRequest()
    {
        var result = await _controller.ApplyExpectedBook("Mistborn", new ApplyExpectedBookDto { AudiobookId = 5 });

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task ApplyExpectedBook_SaveGateBusy_Returns409()
    {
        Assert.IsTrue(_saveGate.TryAcquire(5, out var existingLease));
        try
        {
            var result = await _controller.ApplyExpectedBook(
                "Mistborn", new ApplyExpectedBookDto { AudiobookId = 5, Position = "3", Title = "The Hero of Ages" });

            ProblemAssert.HasDetail(
                result, StatusCodes.Status409Conflict, "A save for audiobook 5 is already in progress.");
            _seriesService.Verify(
                s => s.ApplyMissingBookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long>()),
                Times.Never);
        }
        finally
        {
            existingLease.Dispose();
        }
    }

    [TestMethod]
    public async Task ApplyExpectedBook_Success_ReturnsOkAppliesRechecksAndReleasesGate()
    {
        var result = await _controller.ApplyExpectedBook(
            "Mistborn", new ApplyExpectedBookDto { AudiobookId = 5, Position = "3", Title = "The Hero of Ages" });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _seriesService.Verify(s => s.ApplyMissingBookAsync("Mistborn", "3", "The Hero of Ages", 5), Times.Once);
        _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(5), Times.Once);

        // The controller must release the gate in its finally - prove it by taking the same
        // book's gate again, which has to succeed now.
        Assert.IsTrue(_saveGate.TryAcquire(5, out var reLease), "the apply endpoint must release the save gate");
        reLease.Dispose();
    }

    [TestMethod]
    public async Task ApplyExpectedBook_UnknownExpectedBookOrAudiobook_Returns404()
    {
        _seriesService.Setup(s => s.ApplyMissingBookAsync("Mistborn", "9", "Nope", 5))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.ApplyExpectedBook(
            "Mistborn", new ApplyExpectedBookDto { AudiobookId = 5, Position = "9", Title = "Nope" });

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task ApplyExpectedBook_UnexpectedException_Returns500()
    {
        _seriesService.Setup(s => s.ApplyMissingBookAsync("Mistborn", "3", "The Hero of Ages", 5))
            .ThrowsAsync(new Exception("boom"));

        var result = await _controller.ApplyExpectedBook(
            "Mistborn", new ApplyExpectedBookDto { AudiobookId = 5, Position = "3", Title = "The Hero of Ages" });

        ProblemAssert.HasDetail(
            result, StatusCodes.Status500InternalServerError, ProblemResults.UnexpectedErrorDetail);
    }

    [TestMethod]
    public async Task GetBulkMissingBookCandidates_ValidatesPaging()
    {
        var badPage = await _controller.GetBulkMissingBookCandidates("Mistborn", page: -1, pageSize: 50);
        ProblemAssert.HasStatus(badPage.Result, StatusCodes.Status400BadRequest);

        var badSize = await _controller.GetBulkMissingBookCandidates("Mistborn", page: 0, pageSize: PagingLimits.MaxPageSize + 1);
        ProblemAssert.HasStatus(badSize.Result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task GetBulkMissingBookCandidates_ReturnsMappedPage()
    {
        _seriesService.Setup(s => s.GetBulkMissingBookCandidatesAsync("Mistborn", 0, 50))
            .ReturnsAsync(new SeriesBulkCandidatePage
            {
                Items = new List<SeriesBulkCandidateItem>
                {
                    new()
                    {
                        Book = new SeriesExpectedBookInfo { Id = 11, Title = "The Well of Ascension", Position = "2", Year = 2006 },
                        Candidates = new List<SeriesBookCandidate>
                        {
                            new()
                            {
                                AudiobookId = 5, BookName = "Well of Ascension", Series = null, SeriesPart = null, Year = 2006,
                                Authors = new List<string> { "Brandon Sanderson" }, TitleSimilarity = 0.9, AuthorMatches = true,
                            },
                        },
                    },
                },
                TotalCount = 7,
            });

        var result = await _controller.GetBulkMissingBookCandidates("Mistborn", page: 0, pageSize: 50);

        Assert.AreEqual(7, result.Value!.TotalCount);
        Assert.AreEqual(1, result.Value.Items.Count);
        var item = result.Value.Items[0];
        Assert.AreEqual("2", item.Book.Position);
        Assert.AreEqual(11, item.Book.Id);
        Assert.AreEqual("The Well of Ascension", item.Book.Title);
        Assert.AreEqual(1, item.Candidates.Count);
        var candidate = item.Candidates.Single();
        Assert.AreEqual(5, candidate.AudiobookId);
        Assert.AreEqual("Well of Ascension", candidate.BookName);
        Assert.IsTrue(candidate.AuthorMatches);
    }

    [TestMethod]
    public async Task GetBulkMissingBookCandidates_ServiceThrows_Returns500()
    {
        _seriesService.Setup(s => s.GetBulkMissingBookCandidatesAsync("Mistborn", 0, 50))
            .ThrowsAsync(new Exception("boom"));

        var result = await _controller.GetBulkMissingBookCandidates("Mistborn", page: 0, pageSize: 50);

        ProblemAssert.HasDetail(
            result.Result, StatusCodes.Status500InternalServerError, ProblemResults.UnexpectedErrorDetail);
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_NullOrEmptySelections_ReturnsBadRequest()
    {
        var nullDto = await _controller.StartBulkApplyMissingBooks("Mistborn", null);
        ProblemAssert.HasDetail(nullDto, StatusCodes.Status400BadRequest, "At least one book assignment is required.");

        var emptyDto = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto());
        ProblemAssert.HasDetail(emptyDto, StatusCodes.Status400BadRequest, "At least one book assignment is required.");
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_InvalidSelection_ReturnsBadRequest()
    {
        var zeroId = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto
        {
            Selections = new List<ApplyMissingBookSelectionDto> { new() { AudiobookId = 0, Position = "2" } },
        });
        ProblemAssert.HasDetail(zeroId, StatusCodes.Status400BadRequest, "A valid AudiobookId is required for every assignment.");

        var noKey = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto
        {
            Selections = new List<ApplyMissingBookSelectionDto> { new() { AudiobookId = 5 } },
        });
        ProblemAssert.HasDetail(noKey, StatusCodes.Status400BadRequest, "Position or Title is required for every assignment.");
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_DuplicateAudiobookId_ReturnsBadRequestAndAppliesNothing()
    {
        // Two different missing slots pointing at the same library book would apply the series to
        // that book twice (clobbering the first assignment), so the batch must be refused up front.
        var selections = new List<ApplyMissingBookSelectionDto>
        {
            new() { AudiobookId = 5, Position = "2", Title = "The Well of Ascension" },
            new() { AudiobookId = 5, Position = "3", Title = "The Hero of Ages" },
        };

        var result = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto { Selections = selections });

        ProblemAssert.HasDetail(
            result, StatusCodes.Status400BadRequest,
            "A library book can only be assigned to one missing book: audiobook 5 appears more than once in the request.");
        _seriesService.Verify(
            s => s.ApplyMissingBookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long>()),
            Times.Never, "the request must be rejected before any background apply starts");
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_ReturnsOkImmediately_AndAppliesEachSelectionUnderTheSaveGateWithRecheck()
    {
        var selections = new List<ApplyMissingBookSelectionDto>
        {
            new() { AudiobookId = 5, Position = "2", Title = "The Well of Ascension" },
            new() { AudiobookId = 6, Position = "3", Title = "The Hero of Ages" },
        };

        // Distinct natural keys resolve to distinct roster entries, so the pre-flight duplicate
        // target check lets the batch through.
        _seriesService
            .Setup(s => s.ResolveExpectedBookAsync("Mistborn", "2", "The Well of Ascension"))
            .ReturnsAsync(new SeriesExpectedBookInfo { Id = 11, Title = "The Well of Ascension", Position = "2" });
        _seriesService
            .Setup(s => s.ResolveExpectedBookAsync("Mistborn", "3", "The Hero of Ages"))
            .ReturnsAsync(new SeriesExpectedBookInfo { Id = 12, Title = "The Hero of Ages", Position = "3" });

        var finished = RegisterFinishedWaiter(SeriesController.MissingBookApplyOperationKey);

        var result = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto { Selections = selections });

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _seriesService.Verify(s => s.ApplyMissingBookAsync("Mistborn", "2", "The Well of Ascension", 5), Times.Once);
        _seriesService.Verify(s => s.ApplyMissingBookAsync("Mistborn", "3", "The Hero of Ages", 6), Times.Once);
        _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(5), Times.Once);
        _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(6), Times.Once);
        _clientProxy.Verify(c => c.SeriesMissingBookApplyProgress(It.Is<SeriesMissingBookApplyProgress>(p => p.Processed == 1 && p.Total == 2)), Times.Once);
        _clientProxy.Verify(c => c.SeriesMissingBookApplyProgress(It.Is<SeriesMissingBookApplyProgress>(p => p.Processed == 2 && p.Total == 2)), Times.Once);
        _clientProxy.Verify(c => c.SeriesMissingBookApplyComplete(It.Is<SeriesMissingBookApplyComplete>(p => p.TotalProcessed == 2 && p.TotalSucceeded == 2 && p.TotalFailed == 0)), Times.Once);

        // The controller must release every book's gate - prove it by re-acquiring book 5's.
        Assert.IsTrue(_saveGate.TryAcquire(5, out var reLease), "the bulk apply must release each book's save gate");
        reLease.Dispose();
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_OneItemFails_TheBatchCarriesOn()
    {
        _seriesService.Setup(s => s.ApplyMissingBookAsync("Mistborn", "2", "The Well of Ascension", 5))
            .ThrowsAsync(new Exception("boom"));
        var selections = new List<ApplyMissingBookSelectionDto>
        {
            new() { AudiobookId = 5, Position = "2", Title = "The Well of Ascension" },
            new() { AudiobookId = 6, Position = "3", Title = "The Hero of Ages" },
        };

        var finished = RegisterFinishedWaiter(SeriesController.MissingBookApplyOperationKey);

        var result = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto { Selections = selections });

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesMissingBookApplyComplete(It.Is<SeriesMissingBookApplyComplete>(p => p.TotalProcessed == 2 && p.TotalSucceeded == 1 && p.TotalFailed == 1)), Times.Once);
        _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(5), Times.Never, "a failed apply must not be rechecked");
        _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(6), Times.Once);
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_SaveGateBusy_FailsJustThatItem()
    {
        Assert.IsTrue(_saveGate.TryAcquire(5, out var existingLease));
        try
        {
            var selections = new List<ApplyMissingBookSelectionDto>
            {
                new() { AudiobookId = 5, Position = "2", Title = "The Well of Ascension" },
                new() { AudiobookId = 6, Position = "3", Title = "The Hero of Ages" },
            };

            var finished = RegisterFinishedWaiter(SeriesController.MissingBookApplyOperationKey);

            var result = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto { Selections = selections });

            Assert.IsInstanceOfType(result, typeof(OkResult));

            await AwaitOperationFinished(finished);

            _clientProxy.Verify(c => c.SeriesMissingBookApplyComplete(It.Is<SeriesMissingBookApplyComplete>(p => p.TotalProcessed == 2 && p.TotalSucceeded == 1 && p.TotalFailed == 1)), Times.Once);
            _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(5), Times.Never, "a book another operation holds must not be touched");
            _libraryConsistencyService.Verify(c => c.RecheckAudiobookAsync(6), Times.Once);
        }
        finally
        {
            existingLease.Dispose();
        }
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_RecheckFailure_DoesNotFailTheApply()
    {
        _libraryConsistencyService.Setup(c => c.RecheckAudiobookAsync(5)).ThrowsAsync(new Exception("boom"));
        var selections = new List<ApplyMissingBookSelectionDto>
        {
            new() { AudiobookId = 5, Position = "2", Title = "The Well of Ascension" },
        };

        var finished = RegisterFinishedWaiter(SeriesController.MissingBookApplyOperationKey);

        var result = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto { Selections = selections });

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        // Mirrors the interactive apply: the assignment itself succeeded, so a recheck bug must
        // not turn it into a counted failure.
        _clientProxy.Verify(c => c.SeriesMissingBookApplyComplete(It.Is<SeriesMissingBookApplyComplete>(p => p.TotalSucceeded == 1 && p.TotalFailed == 0)), Times.Once);
    }

    [TestMethod]
    public async Task StartBulkApplyMissingBooks_DuplicateTargetRosterEntry_ReturnsProblemAndStartsNoBackgroundOperation()
    {
        // Two selections address the SAME roster entry under the strict natural-key semantics
        // ApplyMissingBookAsync uses: "2" + "The Well of Ascension" and "2" on its own both
        // resolve to row 42. A keyword-only check on the position/title pairs would miss this
        // (the keys are textually different), so the controller must resolve each key to the
        // stored row and compare the resolved entry ids - before the background operation starts.
        _seriesService
            .Setup(s => s.ResolveExpectedBookAsync("Mistborn", "2", "The Well of Ascension"))
            .ReturnsAsync(new SeriesExpectedBookInfo { Id = 42, Title = "The Well of Ascension", Position = "2" });
        _seriesService
            .Setup(s => s.ResolveExpectedBookAsync("Mistborn", "2", null))
            .ReturnsAsync(new SeriesExpectedBookInfo { Id = 42, Title = "The Well of Ascension", Position = "2" });

        var selections = new List<ApplyMissingBookSelectionDto>
        {
            new() { AudiobookId = 5, Position = "2", Title = "The Well of Ascension" },
            new() { AudiobookId = 6, Position = "2", Title = null },
        };

        var result = await _controller.StartBulkApplyMissingBooks("Mistborn", new ApplyMissingBookBulkRequestDto { Selections = selections });

        ProblemAssert.HasDetail(
            result, StatusCodes.Status400BadRequest,
            "A missing book can only be assigned once: roster entry 'The Well of Ascension' (position '2') is targeted by more than one assignment.");
        _seriesService.Verify(
            s => s.ApplyMissingBookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long>()),
            Times.Never, "the request must be rejected before any background apply starts");
        _statusRegistry.Verify(
            s => s.SetRunning(SeriesController.MissingBookApplyOperationKey),
            Times.Never, "no background operation may start for a batch with a duplicate target");
    }

    // --- Series mapping patterns (owned by this series; target is always the owner's name) ---

    [TestMethod]
    public async Task GetSeriesMappings_ReturnsTheSeriesMappingsAndKeepsNameInTheQueryString()
    {
        _seriesService
            .Setup(s => s.GetSeriesMappingsAsync("Mistborn"))
            .ReturnsAsync(new List<SeriesMapping>
            {
                new(1, "^mistborn.*$", false),
            });

        var result = await _controller.GetSeriesMappings("Mistborn");

        Assert.IsNotNull(result.Value);
        var mappings = result.Value!;
        Assert.AreEqual(1, mappings.Count);
        Assert.AreEqual("^mistborn.*$", mappings[0].Regex);
    }

    [TestMethod]
    public async Task CreateSeriesMapping_RejectsBlankRegexWithProblem()
    {
        var result = await _controller.CreateSeriesMapping("Mistborn", new SeriesMapping(null, "   ", false));

        ProblemAssert.HasDetail(result.Result, StatusCodes.Status400BadRequest, "A regex pattern is required.");
        _seriesService.Verify(
            s => s.CreateSeriesMappingAsync(It.IsAny<string>(), It.IsAny<SeriesMapping>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CreateSeriesMapping_RejectsClientSuppliedId()
    {
        var result = await _controller.CreateSeriesMapping("Mistborn", new SeriesMapping(5, "^x$", false));

        ProblemAssert.HasDetail(result.Result, StatusCodes.Status400BadRequest, "The frontend may not specify an id for a new mapping.");
        _seriesService.Verify(
            s => s.CreateSeriesMappingAsync(It.IsAny<string>(), It.IsAny<SeriesMapping>()),
            Times.Never);
    }

    [TestMethod]
    public async Task CreateSeriesMapping_ReturnsTheSavedMapping()
    {
        _seriesService
            .Setup(s => s.CreateSeriesMappingAsync("Mistborn", It.IsAny<SeriesMapping>()))
            .ReturnsAsync((string _, SeriesMapping m) =>
                new SeriesMapping(42, m.Regex, m.WarnAboutPart));

        var result = await _controller.CreateSeriesMapping(
            "Mistborn", new SeriesMapping(null, "^mistborn.*$", true));

        Assert.IsNotNull(result.Value);
        Assert.AreEqual(42, result.Value!.Id);
        Assert.IsTrue(result.Value!.WarnAboutPart);
    }

    [TestMethod]
    public async Task UpdateSeriesMapping_ReturnsNotFoundWhenTheMappingIsNotOwnedByTheSeries()
    {
        _seriesService
            .Setup(s => s.UpdateSeriesMappingAsync("Mistborn", 7, It.IsAny<SeriesMapping>()))
            .ReturnsAsync((SeriesMapping?)null);

        var result = await _controller.UpdateSeriesMapping(
            7, "Mistborn", new SeriesMapping(null, "^new.*$", false));

        Assert.IsInstanceOfType<NotFoundResult>(result.Result);
    }

    [TestMethod]
    public async Task UpdateSeriesMapping_ReturnsTheUpdatedMapping()
    {
        _seriesService
            .Setup(s => s.UpdateSeriesMappingAsync("Mistborn", 7, It.IsAny<SeriesMapping>()))
            .ReturnsAsync((string _, long _, SeriesMapping m) => new SeriesMapping(7, m.Regex, m.WarnAboutPart));

        var result = await _controller.UpdateSeriesMapping(
            7, "Mistborn", new SeriesMapping(null, "^new.*$", true));

        Assert.IsNotNull(result.Value);
        Assert.AreEqual("^new.*$", result.Value!.Regex);
        Assert.IsTrue(result.Value!.WarnAboutPart);
    }

    [TestMethod]
    public async Task DeleteSeriesMapping_ReturnsNotFoundWhenTheMappingIsNotOwnedByTheSeries()
    {
        _seriesService
            .Setup(s => s.DeleteSeriesMappingAsync("Mistborn", 7))
            .ReturnsAsync(false);

        var result = await _controller.DeleteSeriesMapping(7, "Mistborn");

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task DeleteSeriesMapping_DeletesAnOwnedMapping()
    {
        _seriesService
            .Setup(s => s.DeleteSeriesMappingAsync("Mistborn", 7))
            .ReturnsAsync(true);

        var result = await _controller.DeleteSeriesMapping(7, "Mistborn");

        Assert.IsInstanceOfType<OkResult>(result);
        _seriesService.Verify(s => s.DeleteSeriesMappingAsync("Mistborn", 7), Times.Once);
    }

    [TestMethod]
    public async Task GetFollowStatus_ReflectsTheServiceResult()
    {
        _upcomingReleaseService.Setup(s => s.IsSeriesFollowedByNameAsync("Mistborn")).ReturnsAsync(true);

        var result = await _controller.GetFollowStatus("Mistborn");

        Assert.IsTrue(result.Value!.IsFollowed);
    }

    [TestMethod]
    public async Task FollowSeries_BlankName_ReturnsInvalidRequest()
    {
        var result = await _controller.FollowSeries("");

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "seriesName is required.");
        _upcomingReleaseService.Verify(s => s.FollowSeriesAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task FollowSeries_ValidName_DelegatesToTheService()
    {
        var result = await _controller.FollowSeries("Mistborn");

        Assert.IsInstanceOfType<OkResult>(result);
        _upcomingReleaseService.Verify(s => s.FollowSeriesAsync("Mistborn"), Times.Once);
    }

    [TestMethod]
    public async Task UnfollowSeries_DelegatesToTheService()
    {
        var result = await _controller.UnfollowSeries("Mistborn");

        Assert.IsInstanceOfType<OkResult>(result);
        _upcomingReleaseService.Verify(s => s.UnfollowSeriesAsync("Mistborn"), Times.Once);
    }
}
