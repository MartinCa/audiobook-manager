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
        _logger = new Mock<ILogger<SeriesController>>();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ISeriesService))).Returns(_seriesService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _controller = new SeriesController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            _seriesService.Object,
            _saveGate,
            _libraryConsistencyService.Object,
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
                "Mistborn", ownedSkip: 0, ownedTake: 50, missingSkip: 0, missingTake: 50, ignoredSkip: 0, ignoredTake: 50))
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
                IgnoredBookTotal = 2
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
                "Mistborn", ownedSkip: 0, ownedTake: 50, missingSkip: 0, missingTake: 50, ignoredSkip: 0, ignoredTake: 50))
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
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
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
            s => s.GetSeriesDetailPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
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
    public async Task StartRefreshSeries_ReturnsOkImmediately_AndWiresCompletion()
    {
        _seriesService.Setup(s => s.RefreshSeriesAsync("Mistborn", It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((2, 2, 0, (string?)null));

        var finished = RegisterFinishedWaiter(SeriesController.RefreshOperationKey);

        var result = _controller.StartRefreshSeries("Mistborn");

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SeriesRefreshComplete(It.Is<SeriesRefreshComplete>(p => p.TotalSucceeded == 2)), Times.Once);
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
    public async Task StartRefreshSeries_AlreadyRunning_ReturnsConflict()
    {
        var release = new TaskCompletionSource();
        _seriesService.Setup(s => s.RefreshSeriesAsync("Blocking", It.IsAny<Func<int, int, int, int, Task>>()))
            .Returns(async () =>
            {
                await release.Task;
                return (1, 1, 0, (string?)null);
            });

        var first = _controller.StartRefreshSeries("Blocking");
        Assert.IsInstanceOfType(first, typeof(OkResult));

        var second = _controller.StartRefreshSeries("Other");
        ProblemAssert.HasStatus(second, StatusCodes.Status409Conflict);

        var finished = RegisterFinishedWaiter(SeriesController.RefreshOperationKey);
        release.SetResult();
        await AwaitOperationFinished(finished);
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
}
