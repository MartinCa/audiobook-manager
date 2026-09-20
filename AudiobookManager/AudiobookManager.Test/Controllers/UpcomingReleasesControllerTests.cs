using Microsoft.AspNetCore.Http;
using AudiobookManager.Api;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
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
            7, "Brandon Sanderson", null, null, null, "Hardcover", null, null);
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
