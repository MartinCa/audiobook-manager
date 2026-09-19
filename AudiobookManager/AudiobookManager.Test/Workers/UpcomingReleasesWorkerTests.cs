using System.Reflection;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Workers;
using AudiobookManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Workers;

[TestClass]
public class UpcomingReleasesWorkerTests
{
    private Mock<IUpcomingReleaseService> _upcomingReleaseService = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<ISettingsService> _settingsService = null!;
    private Mock<IScheduledTaskService> _scheduledTaskService = null!;
    private Mock<ILogger<UpcomingReleasesWorker>> _logger = null!;
    private ServiceProvider _serviceProvider = null!;

    // UpcomingReleasesController.RefreshGate is internal, shared process-static state between
    // this worker and the controller's manual "Check Now" endpoint - accessed by reflection here
    // the same way OperationGate does for the controller tests, since the Api assembly does not
    // grant the Test assembly InternalsVisibleTo.
    private static readonly SemaphoreSlim RefreshGate = (SemaphoreSlim)typeof(UpcomingReleasesController)
        .GetField("RefreshGate", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;

    // A schedule that will not fire again for the lifetime of any test run here - used by tests
    // that only care about the immediate startup sweep and must not race a second, real tick.
    private const string FarFutureCron = "0 0 1 1 *";

    [TestInitialize]
    public void Setup()
    {
        _upcomingReleaseService = new Mock<IUpcomingReleaseService>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _settingsService = new Mock<ISettingsService>();
        _scheduledTaskService = new Mock<IScheduledTaskService>();
        _logger = new Mock<ILogger<UpcomingReleasesWorker>>();

        var services = new ServiceCollection();
        services.AddSingleton(_upcomingReleaseService.Object);
        services.AddSingleton(_settingsService.Object);
        services.AddSingleton(_scheduledTaskService.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _serviceProvider.Dispose();
        // The gate is process-static and shared with UpcomingReleasesController - a test that
        // returned before RunOnceAsync's finally block ran would fail the next test with a
        // spurious "gate already held".
        await WaitUntilAsync(() => RefreshGate.CurrentCount == 1, TimeSpan.FromSeconds(5));
    }

    private UpcomingReleasesWorker MakeWorker(TimeSpan? settingsPollInterval = null) => new(
        _serviceProvider,
        _statusRegistry.Object,
        _logger.Object,
        settingsPollInterval);

    private void SetSchedule(bool enabled, string cronSchedule = FarFutureCron) =>
        _settingsService
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings
            {
                UpcomingReleasesEnabled = enabled,
                UpcomingReleasesCronSchedule = cronSchedule,
            });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(20);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }

    // Regression guard: only the manual "Check Now" endpoint used to publish to the operation
    // status registry - a scheduled tick ran the same minutes-long sweep invisibly, so a client
    // polling the shared status key never saw it as running.
    [TestMethod]
    public async Task ExecuteAsync_StartupSweep_PublishesRunningThenFinishedToTheSharedStatusKey()
    {
        SetSchedule(enabled: true);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);

        var worker = MakeWorker();
        await worker.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _statusRegistry.Verify(r => r.SetRunning(UpcomingReleasesController.RefreshOperationKey), Times.Once);
        _statusRegistry.Verify(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey), Times.Once);
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Once);
    }

    // The status must reflect the sweep even when it throws - otherwise a failed sweep would
    // leave the client's "Check Now" button stuck showing "Checking..." forever.
    [TestMethod]
    public async Task ExecuteAsync_SweepThrows_StillPublishesFinished()
    {
        SetSchedule(enabled: true);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync())
            .ThrowsAsync(new InvalidOperationException("Hardcover request failed"));

        var worker = MakeWorker();
        await worker.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _statusRegistry.Verify(r => r.SetRunning(UpcomingReleasesController.RefreshOperationKey), Times.Once);
        _statusRegistry.Verify(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey), Times.Once);
    }

    // A failed sweep records "Failed" plus the exception message - stored server-side for the
    // operator's own Settings page, not a client-facing response, so this is not the
    // "never leak exception detail" case (see AGENTS.md's ProblemResults guidance).
    [TestMethod]
    public async Task ExecuteAsync_SweepThrows_RecordsFailedRunWithTheExceptionMessage()
    {
        SetSchedule(enabled: true);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync())
            .ThrowsAsync(new InvalidOperationException("Hardcover request failed"));

        var worker = MakeWorker();
        await worker.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _scheduledTaskService.Verify(
            s => s.RecordTaskRunAsync(
                ScheduledTaskKeys.UpcomingReleasesRefresh,
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                false,
                "Hardcover request failed"),
            Times.Once);
    }

    // The successful counterpart: "Success" and no error, so the Tasks page doesn't show a stale
    // failure once the next sweep succeeds.
    [TestMethod]
    public async Task ExecuteAsync_SweepSucceeds_RecordsSuccessfulRun()
    {
        SetSchedule(enabled: true);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);

        var worker = MakeWorker();
        await worker.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _scheduledTaskService.Verify(
            s => s.RecordTaskRunAsync(
                ScheduledTaskKeys.UpcomingReleasesRefresh,
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                true,
                null),
            Times.Once);
    }

    // Disabled means no sweep ever runs - but the loop must keep re-checking on a short poll
    // interval (shrunk here well below the real 5-minute default) so re-enabling from the
    // Settings page takes effect without an app restart, rather than returning immediately like
    // the old fixed-interval worker did.
    [TestMethod]
    public async Task ExecuteAsync_CheckDisabled_NeverRunsButKeepsRecheckingTheSetting()
    {
        SetSchedule(enabled: false);

        var worker = MakeWorker(settingsPollInterval: TimeSpan.FromMilliseconds(20));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => _settingsService.Invocations.Count(i => i.Method.Name == nameof(ISettingsService.GetLibrarySettings)) >= 3,
            TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _statusRegistry.Verify(r => r.SetRunning(It.IsAny<string>()), Times.Never);
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Never);
    }

    // Re-enabling (or fixing a hand-edited bad cron) takes effect on the very next poll, without
    // restarting the app - the worker must not have latched the disabled/bad-cron state.
    [TestMethod]
    public async Task ExecuteAsync_ReEnabledAfterPolling_RunsOnTheNextCheck()
    {
        SetSchedule(enabled: false);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);

        var worker = MakeWorker(settingsPollInterval: TimeSpan.FromMilliseconds(20));
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(60); // let it poll the disabled setting a couple of times
        SetSchedule(enabled: true);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Once);
    }

    // Shares UpcomingReleasesController.RefreshGate with the manual "Check Now" endpoint - a tick
    // landing while that gate is already held must skip without touching the status registry
    // (the holder of the gate owns the status for that run).
    [TestMethod]
    public async Task ExecuteAsync_GateAlreadyHeld_SkipsWithoutPublishingStatus()
    {
        SetSchedule(enabled: true);
        RefreshGate.Wait(0);
        try
        {
            // Observable signal for the skip itself (RunOnceAsync's Wait(0) failing and logging),
            // rather than a fixed delay.
            var skipped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _logger
                .Setup(l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(() => skipped.TrySetResult());

            var worker = MakeWorker();

            await worker.StartAsync(CancellationToken.None);
            await skipped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await worker.StopAsync(CancellationToken.None);

            _statusRegistry.Verify(r => r.SetRunning(It.IsAny<string>()), Times.Never);
            _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Never);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    // Regression guard for a cancelled run getting recorded as "Success": the run-cancelled catch
    // block used to `throw;` without setting `error`, so the unconditional `finally` recorded the
    // interrupted run with `error == null` - i.e. succeeded. Cancel the worker's own linked
    // stoppingToken from inside the mocked sweep (BackgroundService.StartAsync links its
    // _stoppingCts to the token passed in, so cancelling `cts` here cancels the very stoppingToken
    // RunOnceAsync observes) so the OperationCanceledException is thrown while
    // stoppingToken.IsCancellationRequested is genuinely true, exactly like a real shutdown mid-sweep.
    [TestMethod]
    public async Task ExecuteAsync_SweepCancelledDuringShutdown_RecordsFailedRunNotSuccess()
    {
        SetSchedule(enabled: true);
        using var cts = new CancellationTokenSource();
        _upcomingReleaseService
            .Setup(s => s.RefreshUpcomingReleasesAsync())
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        var worker = MakeWorker();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(
            () => _scheduledTaskService.Invocations.Any(i => i.Method.Name == nameof(IScheduledTaskService.RecordTaskRunAsync)),
            TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        // Cooperative shutdown must still propagate as cancellation, not be swallowed as a
        // generic failure - the worker's own ExecuteTask must complete cleanly, not fault.
        Assert.IsFalse(worker.ExecuteTask!.IsFaulted);

        _scheduledTaskService.Verify(
            s => s.RecordTaskRunAsync(
                ScheduledTaskKeys.UpcomingReleasesRefresh,
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                false,
                It.Is<string?>(e => e != null)),
            Times.Once);
    }

    // Regression guard: a transient DB error reading the schedule each iteration (e.g. SQLite
    // busy under a concurrent scan) used to propagate straight out of ExecuteAsync uncaught,
    // faulting the BackgroundService - which, with the default
    // IHostOptions.BackgroundServiceExceptionBehavior of StopHost, takes the whole app down. The
    // fix logs and retries on the settings poll interval instead.
    [TestMethod]
    public async Task ExecuteAsync_ScheduleReadThrowsTransientError_LogsAndKeepsPollingWithoutFaultingTheHost()
    {
        _settingsService
            .Setup(s => s.GetLibrarySettings())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var worker = MakeWorker(settingsPollInterval: TimeSpan.FromMilliseconds(20));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => _settingsService.Invocations.Count(i => i.Method.Name == nameof(ISettingsService.GetLibrarySettings)) >= 3,
            TimeSpan.FromSeconds(5));

        Assert.IsFalse(worker.ExecuteTask!.IsFaulted, "A transient settings-read failure must not fault the worker's host task.");

        await worker.StopAsync(CancellationToken.None);

        Assert.IsFalse(worker.ExecuteTask!.IsFaulted);
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Never);
    }

    // Regression guard: the review also flagged RecordTaskRunAsync (in RunOnceAsync's finally) as
    // a second per-iteration DB call that could throw and escape uncaught. Unlike the settings
    // read, this one must be swallowed at its own call site (logged, not rethrown) so a
    // bookkeeping write failure can never mask the sweep's real outcome or fault the host.
    [TestMethod]
    public async Task ExecuteAsync_RecordTaskRunAsyncThrows_DoesNotFaultTheHostOrMaskTheOutcome()
    {
        SetSchedule(enabled: true);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);
        _scheduledTaskService
            .Setup(s => s.RecordTaskRunAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var worker = MakeWorker();
        await worker.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.IsFalse(
            worker.ExecuteTask!.IsFaulted,
            "A failure recording the task run must be swallowed, not fault the worker's host task.");
        _statusRegistry.Verify(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey), Times.Once);
    }

    // Regression guard for schedule changes taking up to a full period to apply: the old worker
    // computed the wait to the next occurrence once and slept the whole span in one Task.Delay,
    // never re-reading the settings until that single delay elapsed - so a saved schedule/enabled
    // change had to wait out the entire previous period. The fix bounds every sleep to the
    // settings poll interval and re-reads on each slice. FarFutureCron's next occurrence (next
    // Jan 1) never actually arrives inside this test's window, so a settings-read count that keeps
    // climbing well past the single post-startup read is only explainable by bounded re-polling.
    [TestMethod]
    public async Task ExecuteAsync_WaitingForFarFutureOccurrence_RePollsSettingsOnBoundedSlicesInsteadOfOneLongDelay()
    {
        SetSchedule(enabled: true, cronSchedule: FarFutureCron);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(UpcomingReleasesController.RefreshOperationKey))
            .Callback(() => finished.TrySetResult());
        _upcomingReleaseService.Setup(s => s.RefreshUpcomingReleasesAsync()).Returns(Task.CompletedTask);

        var worker = MakeWorker(settingsPollInterval: TimeSpan.FromMilliseconds(20));
        await worker.StartAsync(CancellationToken.None);
        // Let the immediate startup sweep (first iteration always runs regardless of the cron) finish.
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var countAfterStartupSweep = _settingsService.Invocations.Count(i => i.Method.Name == nameof(ISettingsService.GetLibrarySettings));

        await WaitUntilAsync(
            () => _settingsService.Invocations.Count(i => i.Method.Name == nameof(ISettingsService.GetLibrarySettings))
                >= countAfterStartupSweep + 3,
            TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        // The far-future occurrence itself never arrives, so repeated polling must not have
        // triggered a second, spurious sweep.
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Once);
    }

    // ComputeNextRun is the pure scheduling decision pulled out of ExecuteAsync specifically so
    // the cron-driven scheduling can be verified without waiting on a real cron tick (which, at
    // minute granularity, could take up to 60 real seconds).
    [TestClass]
    public class ComputeNextRunTests
    {
        [TestMethod]
        public void Disabled_ReturnsNull()
        {
            var result = UpcomingReleasesWorker.ComputeNextRun(false, "0 3 * * *", DateTimeOffset.UtcNow);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void UnparsableCron_ReturnsNull()
        {
            var result = UpcomingReleasesWorker.ComputeNextRun(true, "not a cron expression", DateTimeOffset.UtcNow);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void SecondsExtendedFormat_IsRejected()
        {
            // CronFormat.Standard is 5-field (minute hour day month weekday); a 6-field
            // seconds-extended expression must not silently parse as something else.
            var result = UpcomingReleasesWorker.ComputeNextRun(true, "*/5 * * * * *", DateTimeOffset.UtcNow);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ValidCron_ReturnsTheNextOccurrenceStrictlyAfterNow()
        {
            var now = new DateTimeOffset(2026, 1, 1, 2, 59, 0, TimeSpan.Zero);

            var result = UpcomingReleasesWorker.ComputeNextRun(true, "0 3 * * *", now);

            Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero), result);
        }

        [TestMethod]
        public void ValidCron_AtExactOccurrence_ReturnsTheNextOneNotTheCurrentInstant()
        {
            var now = new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero);

            var result = UpcomingReleasesWorker.ComputeNextRun(true, "0 3 * * *", now);

            Assert.AreEqual(new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.Zero), result);
        }
    }
}
