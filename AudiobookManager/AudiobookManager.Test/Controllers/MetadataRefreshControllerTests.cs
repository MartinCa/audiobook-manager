using Microsoft.AspNetCore.Http;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class MetadataRefreshControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IOrganize> _clientProxy = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<IMetadataRefreshService> _metadataRefreshService = null!;
    private Mock<ILogger<MetadataRefreshController>> _logger = null!;
    private MetadataRefreshController _controller = null!;

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
        _metadataRefreshService = new Mock<IMetadataRefreshService>();
        _logger = new Mock<ILogger<MetadataRefreshController>>();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IMetadataRefreshService)))
            .Returns(_metadataRefreshService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _controller = new MetadataRefreshController(
            _metadataRefreshService.Object,
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            Mock.Of<IHostApplicationLifetime>(),
            _logger.Object);
    }

    // BackgroundOperationRunner calls statusRegistry.SetFinished(key) and THEN releases the
    // static gate in its finally block, so waiting for SetFinished alone can race the gate
    // release (Moq callbacks/TaskCompletionSource can resume our continuation synchronously,
    // inline with the SetFinished call, before the runner's very next statement executes).
    // RunContinuationsAsynchronously keeps that resumption off the runner's thread;
    // AwaitOperationFinished then waits on the gate itself.
    private Task RegisterFinishedWaiter()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry.Setup(r => r.SetFinished(MetadataRefreshController.BulkOperationKey)).Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private static async Task AwaitOperationFinished(Task finishedSignal)
    {
        await finishedSignal.WaitAsync(TimeSpan.FromSeconds(5));
        await OperationGate.WaitUntilReleasedAsync(typeof(MetadataRefreshController));
    }

    [TestMethod]
    public void StartSelectedRefresh_EmptyIds_IsA400_NothingStarts()
    {
        var result = _controller.StartSelectedRefresh(new BulkSelectionDto { AudiobookIds = new List<long>() });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "At least one audiobook must be selected.");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [TestMethod]
    public void StartSelectedRefresh_MoreThanTheCap_IsA400NamingTheCap()
    {
        var ids = new List<long>();
        for (var i = 0; i < 101; i++)
        {
            ids.Add(i + 1);
        }

        var result = _controller.StartSelectedRefresh(new BulkSelectionDto { AudiobookIds = ids });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "No more than 100 audiobooks can be selected at once.");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    // A selected refresh and the all-books refresh are the same operation (same scrapers, same
    // budget) - so they share the gate and the operation key, and a selected refresh must be
    // 409'd while the sweep is running.
    [TestMethod]
    public async Task StartSelectedRefresh_Valid_ReturnsOkAndWiresProgressAndCompletion()
    {
        _metadataRefreshService.Setup(s => s.RefreshSelectedAudiobooksAsync(
                It.IsAny<IReadOnlyList<long>>(), It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((IReadOnlyList<long> _, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 3, 1, 1).GetAwaiter().GetResult();
                return new MetadataRefreshBatchResult(3, 3, 2, 1, StopReason: null);
            });

        var finished = RegisterFinishedWaiter();

        var result = _controller.StartSelectedRefresh(new BulkSelectionDto { AudiobookIds = new List<long> { 1, 2, 3 } });

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _metadataRefreshService.Verify(s => s.RefreshSelectedAudiobooksAsync(
            It.Is<IReadOnlyList<long>>(ids => ids.Count == 3),
            It.IsAny<Func<int, int, int, int, Task>>()), Times.Once);
        _clientProxy.Verify(c => c.MetadataRefreshProgress(It.Is<MetadataRefreshProgress>(p =>
            p.Processed == 1 && p.Total == 3 && p.Succeeded == 1)), Times.Once);
        _clientProxy.Verify(c => c.MetadataRefreshComplete(It.Is<MetadataRefreshComplete>(r =>
            r.TotalProcessed == 3 && r.TotalSucceeded == 2 && r.TotalFailed == 1 &&
            r.StopReason == null)), Times.Once);
    }

    // Regression for the review finding: the refresh service set stopReason internally (Hardcover
    // daily budget exhausted) but dropped it from its return value, so both completion events
    // always carried no stop reason and a daily-limit halt toasted as a plain summary. The reason
    // now travels out of the service and must reach MetadataRefreshComplete on both endpoints.
    [TestMethod]
    public async Task StartSelectedRefresh_StopReasonFromService_IsEmittedOnTheCompleteEvent()
    {
        _metadataRefreshService.Setup(s => s.RefreshSelectedAudiobooksAsync(
                It.IsAny<IReadOnlyList<long>>(), It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync(new MetadataRefreshBatchResult(1, 2, 1, 0, "Hardcover daily API request limit reached"));

        var finished = RegisterFinishedWaiter();

        var result = _controller.StartSelectedRefresh(new BulkSelectionDto { AudiobookIds = new List<long> { 1, 2 } });

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.MetadataRefreshComplete(It.Is<MetadataRefreshComplete>(r =>
            r.TotalProcessed == 1 && r.Total == 2 && r.TotalSucceeded == 1 && r.TotalFailed == 0 &&
            r.StopReason == "Hardcover daily API request limit reached")), Times.Once);
    }

    [TestMethod]
    public async Task StartBulkRefresh_StopReasonFromService_IsEmittedOnTheCompleteEvent()
    {
        _metadataRefreshService.Setup(s => s.RefreshStaleAudiobooksAsync(
                It.IsAny<DateTime?>(), It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync(new MetadataRefreshBatchResult(2, 5, 2, 0, "Hardcover daily API request limit reached"));

        var finished = RegisterFinishedWaiter();

        var result = _controller.StartBulkRefresh(new BulkMetadataRefreshDto(OlderThanUtc: null));

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _metadataRefreshService.Verify(s => s.RefreshStaleAudiobooksAsync(
            It.IsAny<DateTime?>(), It.IsAny<Func<int, int, int, int, Task>>()), Times.Once);
        _clientProxy.Verify(c => c.MetadataRefreshComplete(It.Is<MetadataRefreshComplete>(r =>
            r.TotalProcessed == 2 && r.Total == 5 && r.TotalSucceeded == 2 && r.TotalFailed == 0 &&
            r.StopReason == "Hardcover daily API request limit reached")), Times.Once);
    }
}