using Microsoft.AspNetCore.Http;
using AudiobookManager.Api;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class UpcomingReleasesControllerTests
{
    private Mock<IUpcomingReleaseService> _upcomingReleaseService = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<IScheduledTaskService> _scheduledTaskService = null!;
    private UpcomingReleasesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _upcomingReleaseService = new Mock<IUpcomingReleaseService>();
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _scheduledTaskService = new Mock<IScheduledTaskService>();

        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IUpcomingReleaseService)))
            .Returns(_upcomingReleaseService.Object);
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IScheduledTaskService)))
            .Returns(_scheduledTaskService.Object);
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        _controller = new UpcomingReleasesController(
            _upcomingReleaseService.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            Mock.Of<IHostApplicationLifetime>(),
            Mock.Of<ILogger<UpcomingReleasesController>>());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        // The gate is process-static, so a test that returned before the background task's
        // finally block ran would fail the next one with a spurious Conflict.
        await OperationGate.WaitUntilReleasedAsync(typeof(UpcomingReleasesController));
    }

    private Task RegisterFinishedWaiter()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    [TestMethod]
    public async Task GetUpcomingReleases_LimitBelowOne_ReturnsInvalidRequest()
    {
        var result = await _controller.GetUpcomingReleases(limit: 0);

        ProblemAssert.HasDetail(
            result.Result,
            StatusCodes.Status400BadRequest,
            $"limit must be between 1 and {PagingLimits.MaxPageSize}.");
        _upcomingReleaseService.Verify(
            s => s.GetUpcomingReleasesAsync(It.IsAny<long?>(), It.IsAny<long?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetUpcomingReleases_LimitAboveMax_ReturnsInvalidRequest()
    {
        var result = await _controller.GetUpcomingReleases(limit: PagingLimits.MaxPageSize + 1);

        ProblemAssert.HasDetail(
            result.Result,
            StatusCodes.Status400BadRequest,
            $"limit must be between 1 and {PagingLimits.MaxPageSize}.");
    }

    [TestMethod]
    public async Task GetUpcomingReleases_NegativeOffset_ReturnsInvalidRequest()
    {
        var result = await _controller.GetUpcomingReleases(offset: -1);

        ProblemAssert.HasDetail(
            result.Result,
            StatusCodes.Status400BadRequest,
            $"offset must be between 0 and {PagingLimits.MaxPageOffset}.");
    }

    [TestMethod]
    public async Task GetUpcomingReleases_OffsetAboveMax_ReturnsInvalidRequest()
    {
        var result = await _controller.GetUpcomingReleases(offset: (int)PagingLimits.MaxPageOffset + 1);

        ProblemAssert.HasDetail(
            result.Result,
            StatusCodes.Status400BadRequest,
            $"offset must be between 0 and {PagingLimits.MaxPageOffset}.");
    }

    [TestMethod]
    public async Task GetUpcomingReleases_MapsResultsAndPassesFiltersThrough()
    {
        var item = new UpcomingReleaseItem(
            UpcomingReleaseSource.Legacy, 1, "The Stormlight Archive 6", new DateOnly(2030, 1, 1), 2030,
            7, "Brandon Sanderson", null, null, null, "Hardcover", null, null, "999");
        _upcomingReleaseService
            .Setup(s => s.GetUpcomingReleasesAsync(7, null, 50, 0))
            .ReturnsAsync((new List<UpcomingReleaseItem> { item }, 1));

        var result = await _controller.GetUpcomingReleases(authorId: 7);

        Assert.AreEqual(1, result.Value!.Count);
        Assert.AreEqual(1, result.Value!.Total);
        Assert.AreEqual("The Stormlight Archive 6", result.Value!.Items[0].Title);
        Assert.AreEqual("Brandon Sanderson", result.Value!.Items[0].AuthorName);
        _upcomingReleaseService.Verify(s => s.GetUpcomingReleasesAsync(7, null, 50, 0), Times.Once);
    }

    [TestMethod]
    public async Task RemoveUpcomingRelease_Found_ReturnsOk()
    {
        _upcomingReleaseService.Setup(s => s.RemoveUpcomingReleaseAsync(5)).ReturnsAsync(true);

        var result = await _controller.RemoveUpcomingRelease(5);

        Assert.IsInstanceOfType<OkResult>(result);
    }

    [TestMethod]
    public async Task RemoveUpcomingRelease_NotFound_Returns404()
    {
        _upcomingReleaseService.Setup(s => s.RemoveUpcomingReleaseAsync(5)).ReturnsAsync(false);

        var result = await _controller.RemoveUpcomingRelease(5);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    // --- Dismiss-roster addressing -------------------------------------------

    // The id route is preferred: a roster-derived item carries its stable expected-book row id,
    // which names the exact shared row - no scope or title ambiguity.
    [TestMethod]
    public async Task DismissRosterUpcomingRelease_ById_DismissesTheExactRow()
    {
        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            ExpectedBookId = 42,
            SourceName = "Hardcover",
            SourceBookId = "555",
            Title = "Words of Radiance",
        });

        Assert.IsInstanceOfType<OkResult>(result);
        _upcomingReleaseService.Verify(s => s.DismissAuthorRosterUpcomingByIdAsync(42), Times.Once);
        _upcomingReleaseService.Verify(
            s => s.DismissRosterUpcomingBySourceAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never,
            "the id route wins over the source identity and the title fallback");
        _upcomingReleaseService.Verify(
            s => s.DismissAuthorRosterUpcomingAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        _upcomingReleaseService.Verify(
            s => s.DismissSeriesRosterUpcomingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingRelease_ById_UnknownRow_Returns404()
    {
        _upcomingReleaseService.Setup(s => s.DismissAuthorRosterUpcomingByIdAsync(999))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            ExpectedBookId = 999,
        });

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    // A client that only carries the source identity (the dedup key the merged item exposes) can
    // dismiss the exact row it came from without the id.
    [TestMethod]
    public async Task DismissRosterUpcomingRelease_BySourceIdentity_DismissesTheMatchingRow()
    {
        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            SourceName = "Hardcover",
            SourceBookId = "555",
        });

        Assert.IsInstanceOfType<OkResult>(result);
        _upcomingReleaseService.Verify(
            s => s.DismissRosterUpcomingBySourceAsync("Hardcover", "555"), Times.Once);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingRelease_BySourceIdentity_UnknownIdentity_Returns404()
    {
        _upcomingReleaseService
            .Setup(s => s.DismissRosterUpcomingBySourceAsync("Hardcover", "nope"))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            SourceName = "Hardcover",
            SourceBookId = "nope",
        });

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingRelease_NullBody_ReturnsInvalidRequest()
    {
        var result = await _controller.DismissRosterUpcomingRelease(null);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "A request body is required.");
    }

    // Legacy callers keep the title path: series scope by name+position, author scope by id.
    [TestMethod]
    public async Task DismissRosterUpcomingRelease_SeriesTitleFallback_StillWorks()
    {
        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            SeriesName = "Mistborn",
            SeriesPosition = "5",
            Title = "The Lost Metal",
        });

        Assert.IsInstanceOfType<OkResult>(result);
        _upcomingReleaseService.Verify(
            s => s.DismissSeriesRosterUpcomingAsync("Mistborn", "5", "The Lost Metal"), Times.Once);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingRelease_AuthorTitleFallback_StillWorks()
    {
        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            AuthorId = 9,
            Title = "Standalone Novella",
        });

        Assert.IsInstanceOfType<OkResult>(result);
        _upcomingReleaseService.Verify(s => s.DismissAuthorRosterUpcomingAsync(9, "Standalone Novella"), Times.Once);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingRelease_NoIdentityAndBlankTitle_ReturnsInvalidRequest()
    {
        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto());

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Title is required.");
        _upcomingReleaseService.Verify(
            s => s.DismissAuthorRosterUpcomingAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingRelease_NoScope_ReturnsInvalidRequest()
    {
        var result = await _controller.DismissRosterUpcomingRelease(new DismissRosterUpcomingReleaseDto
        {
            Title = "The Lost Metal",
        });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Exactly one of seriesName or authorId must be set.");
    }

    [TestMethod]
    public async Task RefreshUpcomingReleases_RunsTheSweepAndReturnsOk()
    {
        var finished = RegisterFinishedWaiter();
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);

        var result = _controller.RefreshUpcomingReleases();

        Assert.IsInstanceOfType<OkResult>(result);
        await finished.WaitAsync(TimeSpan.FromSeconds(5));
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Once);
    }

    // Shares the same scheduled_task_runs row as UpcomingReleasesWorker's own tick - a user who
    // always triggers refreshes manually should still see an accurate "last run" on the Settings
    // Tasks page, not "never run".
    [TestMethod]
    public async Task RefreshUpcomingReleases_RecordsTheRunOnTheSharedScheduledTaskKey()
    {
        var finished = RegisterFinishedWaiter();
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);

        _controller.RefreshUpcomingReleases();
        await finished.WaitAsync(TimeSpan.FromSeconds(5));

        _scheduledTaskService.Verify(
            s => s.RecordTaskRunAsync(
                ScheduledTaskKeys.UpcomingReleasesRefresh,
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                true,
                null),
            Times.Once);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleases_ReturnsConflictWhileOneIsAlreadyRunning()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = RegisterFinishedWaiter();
        _upcomingReleaseService
            .Setup(s => s.RefreshUpcomingReleasesAsync())
            .Returns(async () =>
            {
                started.TrySetResult();
                await release.Task;
            });

        var first = _controller.RefreshUpcomingReleases();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = _controller.RefreshUpcomingReleases();

        Assert.IsInstanceOfType<OkResult>(first);
        ProblemAssert.HasStatus(second, StatusCodes.Status409Conflict);
        // A second concurrent sweep would poll the same followed authors/series a second time,
        // doubling the Hardcover request spend for the same discoveries - this is exactly the
        // gate the manual endpoint shares with the periodic worker.
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Once);

        release.TrySetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
