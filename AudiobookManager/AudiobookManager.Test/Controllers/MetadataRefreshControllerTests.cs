using Microsoft.AspNetCore.Http;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
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
    private SingleBookOperationTracker _tracker = null!;
    private AudiobookSaveGate _saveGate = null!;
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

        _tracker = new SingleBookOperationTracker();
        _saveGate = new AudiobookSaveGate();

        _controller = new MetadataRefreshController(
            _metadataRefreshService.Object,
            _tracker,
            _saveGate,
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

    // Same pattern as RegisterFinishedWaiter, but keyed to ApplyOperationKey - apply-selected/
    // apply-filtered run under the separate _applyLock/ApplyOperationKey, not the refresh
    // bulk-operation's BulkOperationKey.
    private Task RegisterApplyFinishedWaiter()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry.Setup(r => r.SetFinished(MetadataRefreshController.ApplyOperationKey)).Callback(() => tcs.TrySetResult());
        return tcs.Task;
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

    // The single-book apply is fire-and-forget: it answers 202 and reports its outcome as a
    // SingleBookOperationComplete event, because rewriting the m4b (tags, relocation, sidecars) is
    // too slow to hold a request open for. These helpers wait on that event rather than sleeping.
    private Task<SingleBookOperationComplete> NextCompletion()
    {
        var tcs = new TaskCompletionSource<SingleBookOperationComplete>(TaskCreationOptions.RunContinuationsAsynchronously);
        _clientProxy.Setup(c => c.SingleBookOperationComplete(It.IsAny<SingleBookOperationComplete>()))
            .Callback<SingleBookOperationComplete>(e => tcs.TrySetResult(e))
            .Returns(Task.CompletedTask);
        return tcs.Task;
    }

    [TestMethod]
    public async Task ApplyPending_Valid_Returns202AndReportsCompletedOverSignalR()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(50, null, false, null, false)).ReturnsAsync(true);

        var result = _controller.ApplyPending(50, null);

        Assert.IsInstanceOfType<AcceptedResult>(result);
        var outcome = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(new SingleBookOperationComplete(50, SingleBookOperations.PendingRefreshApply, SingleBookOperationStatus.Completed, null), outcome);
    }

    [TestMethod]
    public async Task ApplyPending_NothingPending_ReportsNothingToApply()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(51, null, false, null, false)).ReturnsAsync(false);

        _controller.ApplyPending(51, null);

        var outcome = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(SingleBookOperationStatus.NothingToApply, outcome.Status);
    }

    // The gate is taken inside ApplyOneAsync, so a book that was free when the request arrived but
    // is busy by the time the task runs is a reported failure, not an unobserved exception.
    [TestMethod]
    public async Task ApplyPending_BookBecomesBusyBeforeTheApplyRuns_ReportsFailedWithTheMessage()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(55, null, false, null, false))
            .ThrowsAsync(new AudiobookBusyException(55));

        _controller.ApplyPending(55, null);

        var outcome = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(SingleBookOperationStatus.Failed, outcome.Status);
        Assert.AreEqual("Another operation is already modifying audiobook 55", outcome.Message);
    }

    [TestMethod]
    public void ApplyPending_BookAlreadyBusy_Returns409WithoutStartingAnything()
    {
        Assert.IsTrue(_saveGate.TryAcquire(56, out var heldByAnotherOperation));
        using (heldByAnotherOperation)
        {
            var result = _controller.ApplyPending(56, null);

            ProblemAssert.HasDetail(result, StatusCodes.Status409Conflict,
                "Another operation is already modifying audiobook 56.");
        }

        _metadataRefreshService.Verify(
            s => s.ApplyPendingRefreshAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Never);
        Assert.IsFalse(_tracker.IsRunning(SingleBookOperations.PendingRefreshApply, 56), "A refused request must not leave its slot taken.");
    }

    [TestMethod]
    public async Task ApplyPending_SecondApplyForTheSameBookWhileFirstRuns_Returns409AndRunsOnce()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(57, null, false, null, false))
            .Returns(async () =>
            {
                started.TrySetResult();
                return await release.Task;
            });

        Assert.IsInstanceOfType<AcceptedResult>(_controller.ApplyPending(57, null));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = _controller.ApplyPending(57, null);

        ProblemAssert.HasDetail(second, StatusCodes.Status409Conflict, "An apply for audiobook 57 is already in progress.");
        release.SetResult(true);
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        _metadataRefreshService.Verify(s => s.ApplyPendingRefreshAsync(57, null, false, null, false), Times.Once);
    }

    [TestMethod]
    public async Task ApplyPending_Finished_ReleasesTheSlotSoTheBookCanBeAppliedAgain()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(58, null, false, null, false)).ReturnsAsync(true);

        _controller.ApplyPending(58, null);
        await completion.WaitAsync(TimeSpan.FromSeconds(5));

        // The slot is released before the event is sent, so by now it is free.
        Assert.IsFalse(_tracker.IsRunning(SingleBookOperations.PendingRefreshApply, 58));
        Assert.IsInstanceOfType<AcceptedResult>(_controller.ApplyPending(58, null));
    }

    // Regression: a file already at the target path used to escape as a bare System.Exception and
    // surface as a nondescript 500, leaving the user with no message and no way to resolve it.
    [TestMethod]
    public async Task ApplyPending_TargetFileAlreadyExists_ReportsTargetExistsWithTheMessage()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(59, null, false, null, false))
            .ThrowsAsync(new TargetPathExistsException("/library/A/Book.m4b"));

        _controller.ApplyPending(59, null);

        var outcome = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(SingleBookOperationStatus.TargetExists, outcome.Status);
        StringAssert.Contains(outcome.Message, "'/library/A/Book.m4b' already exists");
    }

    // An unexpected exception's text routinely contains absolute container paths and the event is
    // broadcast to every client, so it is logged and replaced by a fixed sentence.
    [TestMethod]
    public async Task ApplyPending_UnexpectedException_ReportsFixedSentenceNotTheExceptionText()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(60, null, false, null, false))
            .ThrowsAsync(new IOException("Could not open /data/library/Secret Author/Book.m4b"));

        _controller.ApplyPending(60, null);

        var outcome = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(SingleBookOperationStatus.Failed, outcome.Status);
        Assert.IsFalse(outcome.Message!.Contains("/data/library"), outcome.Message);
    }

    [TestMethod]
    public async Task ApplyPending_ReplaceExistingInDto_PassesThroughToTheService()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(61, null, false, null, true))
            .ReturnsAsync(true);

        _controller.ApplyPending(61, new ApplyPendingRefreshDto(null, ReplaceExisting: true));

        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        _metadataRefreshService.Verify(s => s.ApplyPendingRefreshAsync(61, null, false, null, true), Times.Once);
    }

    [TestMethod]
    public async Task CheckApplyTarget_Collision_ReturnsTheExistingFileDetails()
    {
        _metadataRefreshService.Setup(s => s.CheckApplyTargetCollisionAsync(58, null, false, null))
            .ReturnsAsync(new TargetPathCollisionResult
            {
                TargetPath = "/library/A/Book.m4b",
                Exists = true,
                ExistingAudiobookId = 9,
                ExistingSizeInBytes = 123,
                ExistingDurationInSeconds = 456,
            });

        var result = await _controller.CheckApplyTarget(58, null);

        var dto = (result.Result as OkObjectResult)?.Value as TargetPathCheckDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual("/library/A/Book.m4b", dto.TargetPath);
        Assert.IsTrue(dto.Exists);
        Assert.AreEqual(9, dto.Existing!.AudiobookId);
        Assert.AreEqual(123, dto.Existing.SizeInBytes);
        Assert.AreEqual(456, dto.Existing.DurationInSeconds);
    }

    [TestMethod]
    public async Task CheckApplyTarget_NothingToCheck_Returns204()
    {
        _metadataRefreshService.Setup(s => s.CheckApplyTargetCollisionAsync(59, null, false, null))
            .ReturnsAsync((TargetPathCollisionResult?)null);

        var result = await _controller.CheckApplyTarget(59, null);

        Assert.IsInstanceOfType<NoContentResult>(result.Result);
    }

    // The service throws (rather than returning false) for an unreadable payload, so the user is
    // told why instead of being shown "nothing to apply". The regression guard for the throw itself is
    // MetadataRefreshServiceTests.ApplyPendingRefreshAsync_UnparseablePayload_ThrowsInsteadOfReturningFalse;
    // this one pins that the message still reaches the user now that the request has already returned.
    [TestMethod]
    public async Task ApplyPending_UnparseablePayload_ReportsFailedWithTheMessageNotNothingToApply()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(77, null, false, null, false))
            .ThrowsAsync(new InvalidOperationException("The pending metadata refresh for audiobook 77 could not be read; its stored payload is not valid."));

        _controller.ApplyPending(77, null);

        var outcome = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(SingleBookOperationStatus.Failed, outcome.Status);
        Assert.AreEqual("The pending metadata refresh for audiobook 77 could not be read; its stored payload is not valid.", outcome.Message);
    }

    // Regression for a review finding on the splitTitleOnColon toggle: the apply endpoints are thin
    // pass-throughs from their DTOs to the service, and nothing asserted the flag actually reaches
    // the service call rather than being silently dropped along the way.
    [TestMethod]
    public async Task ApplyPending_SplitTitleOnColonInDto_PassesThroughToTheService()
    {
        var completion = NextCompletion();
        _metadataRefreshService.Setup(s => s.ApplyPendingRefreshAsync(88, null, true, null, false))
            .ReturnsAsync(true);

        _controller.ApplyPending(88, new ApplyPendingRefreshDto(null, SplitTitleOnColon: true));

        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        _metadataRefreshService.Verify(s => s.ApplyPendingRefreshAsync(88, null, true, null, false), Times.Once);
    }

    [TestMethod]
    public async Task StartApplySelected_SplitTitleOnColonInDto_PassesThroughToTheService()
    {
        _metadataRefreshService.Setup(s => s.ApplySelectedPendingRefreshesAsync(
                It.IsAny<IReadOnlyList<long>>(), It.IsAny<Func<int, int, int, int, Task>>(), true))
            .ReturnsAsync((2, 2, 0));

        var finished = RegisterApplyFinishedWaiter();

        var result = _controller.StartApplySelected(
            new ApplySelectedMetadataRefreshDto(new List<long> { 1, 2 }, SplitTitleOnColon: true));

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _metadataRefreshService.Verify(s => s.ApplySelectedPendingRefreshesAsync(
            It.Is<IReadOnlyList<long>>(ids => ids.SequenceEqual(new List<long> { 1, 2 })),
            It.IsAny<Func<int, int, int, int, Task>>(),
            true), Times.Once);
    }

    [TestMethod]
    public async Task StartApplyFiltered_SplitTitleOnColonInDto_PassesThroughToTheService()
    {
        _metadataRefreshService.Setup(s => s.ApplyFilteredPendingRefreshesAsync(
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Func<int, int, int, int, Task>>(), true,
                It.IsAny<IReadOnlyCollection<string>?>()))
            .ReturnsAsync((3, 3, 0));

        var finished = RegisterApplyFinishedWaiter();

        var result = _controller.StartApplyFiltered(
            new BulkApplyFilteredMetadataRefreshDto(new List<string> { "BookName" }, SplitTitleOnColon: true));

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _metadataRefreshService.Verify(s => s.ApplyFilteredPendingRefreshesAsync(
            It.Is<IReadOnlyCollection<string>?>(f => f != null && f.SequenceEqual(new List<string> { "BookName" })),
            It.IsAny<Func<int, int, int, int, Task>>(),
            true,
            null), Times.Once);
    }

    [TestMethod]
    public async Task StartApplyFiltered_SourcesInDto_PassThroughToTheService()
    {
        _metadataRefreshService.Setup(s => s.ApplyFilteredPendingRefreshesAsync(
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Func<int, int, int, int, Task>>(),
                false, It.IsAny<IReadOnlyCollection<string>?>()))
            .ReturnsAsync((1, 1, 0));

        var finished = RegisterApplyFinishedWaiter();

        var result = _controller.StartApplyFiltered(
            new BulkApplyFilteredMetadataRefreshDto(new List<string> { "BookName" }, Sources: new List<string> { "Audible" }));

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _metadataRefreshService.Verify(s => s.ApplyFilteredPendingRefreshesAsync(
            It.Is<IReadOnlyCollection<string>?>(f => f != null && f.SequenceEqual(new List<string> { "BookName" })),
            It.IsAny<Func<int, int, int, int, Task>>(),
            false,
            It.Is<IReadOnlyCollection<string>?>(src => src != null && src.SequenceEqual(new List<string> { "Audible" }))), Times.Once);
    }

    [TestMethod]
    public async Task StartApplyFiltered_SourceOnly_IsAcceptedWithNoFields()
    {
        _metadataRefreshService.Setup(s => s.ApplyFilteredPendingRefreshesAsync(
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Func<int, int, int, int, Task>>(),
                false, It.IsAny<IReadOnlyCollection<string>?>()))
            .ReturnsAsync((1, 1, 0));

        var finished = RegisterApplyFinishedWaiter();

        var result = _controller.StartApplyFiltered(
            new BulkApplyFilteredMetadataRefreshDto(null, Sources: new List<string> { "Audible" }));

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _metadataRefreshService.Verify(s => s.ApplyFilteredPendingRefreshesAsync(
            null,
            It.IsAny<Func<int, int, int, int, Task>>(),
            false,
            It.Is<IReadOnlyCollection<string>?>(src => src != null && src.SequenceEqual(new List<string> { "Audible" }))), Times.Once);
    }

    [TestMethod]
    public void StartApplyFiltered_NoFieldsAndNoSources_ReturnsBadRequest()
    {
        var result = _controller.StartApplyFiltered(
            new BulkApplyFilteredMetadataRefreshDto(new List<string>(), Sources: new List<string>()));

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "At least one field or source must be selected.");
        _metadataRefreshService.Verify(s => s.ApplyFilteredPendingRefreshesAsync(
            It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<Func<int, int, int, int, Task>>(),
            It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>?>()), Times.Never);
    }

    [TestMethod]
    public async Task GetPendingForAudiobook_NoPendingSnapshot_Returns204()
    {
        // 204 rather than 404: absence is the normal state the book page polls on every visit,
        // and the browser logs every non-2xx response as a console error that no client-side
        // handling can silence.
        _metadataRefreshService.Setup(s => s.GetPendingRefreshAsync(1779))
            .ReturnsAsync(((PendingMetadataRefresh, PendingRefreshPayload.Snapshot)?)(null));

        var result = await _controller.GetPendingForAudiobook(1779);

        Assert.IsInstanceOfType(result.Result, typeof(NoContentResult));
    }

    [TestMethod]
    public async Task ReevaluatePending_ReturnsTheServiceResultAsDto()
    {
        _metadataRefreshService.Setup(s => s.ReevaluatePendingRefreshesAsync())
            .ReturnsAsync(new MetadataRefreshReevaluateResult(5, 2, 1, Settleable: 1));

        var finished = RegisterApplyFinishedWaiter();

        var result = await _controller.ReevaluatePending();
        await AwaitOperationFinished(finished);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var dto = ok.Value as MetadataRefreshReevaluateResultDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual(5, dto.Processed);
        Assert.AreEqual(2, dto.Updated);
        Assert.AreEqual(1, dto.Removed);
    }

    [TestMethod]
    public async Task ReevaluatePending_NothingTheRulesSettle_StartsNoApplyAndReportsSo()
    {
        // Regression (review finding): the flag used to mean "the apply lock was free", so the client
        // announced an apply that would have done nothing.
        _metadataRefreshService.Setup(s => s.ReevaluatePendingRefreshesAsync())
            .ReturnsAsync(new MetadataRefreshReevaluateResult(3, 0, 0, Settleable: 0));

        var result = await _controller.ReevaluatePending();

        var dto = (result.Result as OkObjectResult)?.Value as MetadataRefreshReevaluateResultDto;
        Assert.IsNotNull(dto);
        Assert.IsFalse(dto.AutoApplyStarted);
        _metadataRefreshService.Verify(s => s.ApplyPendingByRulesAsync(It.IsAny<Func<int, int, int, int, Task>>()), Times.Never);
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [TestMethod]
    public async Task ReevaluatePending_StartsTheRuleBasedApplyInTheBackground()
    {
        _metadataRefreshService.Setup(s => s.ReevaluatePendingRefreshesAsync())
            .ReturnsAsync(new MetadataRefreshReevaluateResult(2, 0, 0, Settleable: 2));
        _metadataRefreshService.Setup(s => s.ApplyPendingByRulesAsync(It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((0, 0, 0));

        var finished = RegisterApplyFinishedWaiter();

        var result = await _controller.ReevaluatePending();
        await AwaitOperationFinished(finished);

        var dto = (result.Result as OkObjectResult)?.Value as MetadataRefreshReevaluateResultDto;
        Assert.IsNotNull(dto);
        Assert.IsTrue(dto.AutoApplyStarted);
        _metadataRefreshService.Verify(s => s.ApplyPendingByRulesAsync(It.IsAny<Func<int, int, int, int, Task>>()), Times.Once);
    }

    [TestMethod]
    public async Task GetPending_PassesFieldsAndSourcesThroughToTheService()
    {
        _metadataRefreshService
            .Setup(s => s.GetPendingPageAsync(0, 20, It.IsAny<List<string>>(), It.IsAny<List<string>>()))
            .ReturnsAsync((new List<PendingMetadataRefresh>(), 0));

        await _controller.GetPending(0, 20, new List<string> { "Rating" }, new List<string> { "Audible" });

        _metadataRefreshService.Verify(
            s => s.GetPendingPageAsync(
                0, 20,
                It.Is<List<string>>(f => f.SequenceEqual(new List<string> { "Rating" })),
                It.Is<List<string>>(src => src.SequenceEqual(new List<string> { "Audible" }))),
            Times.Once);
    }

    [TestMethod]
    public async Task GetPendingSummary_PassesFieldsAndSourcesThroughToTheService()
    {
        _metadataRefreshService
            .Setup(s => s.GetPendingAudiobookIdsAsync(It.IsAny<List<string>>(), It.IsAny<List<string>>()))
            .ReturnsAsync(new List<long>());

        await _controller.GetPendingSummary(new List<string> { "Rating" }, new List<string> { "Audible" });

        _metadataRefreshService.Verify(
            s => s.GetPendingAudiobookIdsAsync(
                It.Is<List<string>>(f => f.SequenceEqual(new List<string> { "Rating" })),
                It.Is<List<string>>(src => src.SequenceEqual(new List<string> { "Audible" }))),
            Times.Once);
    }

    [TestMethod]
    public async Task DismissSelected_EmptyIds_IsA400_ServiceNeverCalled()
    {
        var result = await _controller.DismissSelected(new BulkSelectionDto { AudiobookIds = new List<long>() });

        ProblemAssert.HasDetail(result.Result!, StatusCodes.Status400BadRequest, "At least one audiobook must be selected.");
        _metadataRefreshService.Verify(
            s => s.DismissSelectedPendingRefreshesAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
    }

    [TestMethod]
    public async Task DismissSelected_MoreThanTheCap_IsA400NamingTheCap()
    {
        var ids = new List<long>();
        for (var i = 0; i < 101; i++)
        {
            ids.Add(i + 1);
        }

        var result = await _controller.DismissSelected(new BulkSelectionDto { AudiobookIds = ids });

        ProblemAssert.HasDetail(result.Result!, StatusCodes.Status400BadRequest, "No more than 100 audiobooks can be selected at once.");
        _metadataRefreshService.Verify(
            s => s.DismissSelectedPendingRefreshesAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
    }

    [TestMethod]
    public async Task DismissSelected_ValidSelection_ReturnsTheDismissedCount()
    {
        var ids = new List<long> { 11, 12, 13 };
        _metadataRefreshService.Setup(s => s.DismissSelectedPendingRefreshesAsync(ids)).ReturnsAsync(2);

        var result = await _controller.DismissSelected(new BulkSelectionDto { AudiobookIds = ids });

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var dto = ok.Value as DismissSelectedMetadataRefreshResultDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual(2, dto.Dismissed);
    }
}