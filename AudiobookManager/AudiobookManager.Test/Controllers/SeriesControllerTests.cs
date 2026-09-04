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
    public async Task GetAllSeries_ReturnsMappedDtoList()
    {
        _seriesService.Setup(s => s.GetAllSeriesOverviewAsync()).ReturnsAsync(new List<SeriesOverview> { MakeOverview() });

        var result = await _controller.GetAllSeries();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Mistborn", result[0].Name);
        Assert.AreEqual(3, result[0].OwnedBookCount);
        Assert.IsTrue(result[0].IsMatched);
    }

    [TestMethod]
    public async Task GetSeriesDetail_Found_ReturnsMappedDto()
    {
        var detail = new SeriesDetail
        {
            Overview = MakeOverview(),
            OwnedBooks = new List<SeriesOwnedBook> { new SeriesOwnedBook { Id = 1, BookName = "The Final Empire", Year = 2006, Authors = new List<string> { "Brandon Sanderson" }, Narrators = new List<string>() } },
            MissingBooks = new List<SeriesExpectedBookInfo> { new SeriesExpectedBookInfo { Id = 10, Title = "Missing Book", Position = "4" } },
            IgnoredBooks = new List<SeriesExpectedBookInfo>()
        };
        _seriesService.Setup(s => s.GetSeriesDetailAsync("Mistborn")).ReturnsAsync(detail);

        var result = await _controller.GetSeriesDetail("Mistborn");

        Assert.IsNotNull(result.Value);
        var dto = result.Value!;
        Assert.AreEqual("Mistborn", dto.Overview.Name);
        Assert.AreEqual(1, dto.OwnedBooks.Count);
        Assert.AreEqual(1, dto.MissingBooks.Count);
        Assert.AreEqual("Missing Book", dto.MissingBooks[0].Title);
    }

    [TestMethod]
    public async Task GetSeriesDetail_NotFound_Returns404()
    {
        _seriesService.Setup(s => s.GetSeriesDetailAsync("Unknown")).ReturnsAsync((SeriesDetail?)null);

        var result = await _controller.GetSeriesDetail("Unknown");

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
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
}
