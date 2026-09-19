using System.Reflection;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Workers;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Workers;

[TestClass]
public class UpcomingReleasesWorkerTests
{
    private Mock<IUpcomingReleaseService> _upcomingReleaseService = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<ILogger<UpcomingReleasesWorker>> _logger = null!;
    private ServiceProvider _serviceProvider = null!;

    // UpcomingReleasesController.RefreshGate is internal, shared process-static state between
    // this worker and the controller's manual "Check Now" endpoint - accessed by reflection here
    // the same way OperationGate does for the controller tests, since the Api assembly does not
    // grant the Test assembly InternalsVisibleTo.
    private static readonly SemaphoreSlim RefreshGate = (SemaphoreSlim)typeof(UpcomingReleasesController)
        .GetField("RefreshGate", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;

    [TestInitialize]
    public void Setup()
    {
        _upcomingReleaseService = new Mock<IUpcomingReleaseService>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _logger = new Mock<ILogger<UpcomingReleasesWorker>>();

        var services = new ServiceCollection();
        services.AddSingleton(_upcomingReleaseService.Object);
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

    private UpcomingReleasesWorker MakeWorker(bool checkEnabled = true) => new(
        _serviceProvider,
        _statusRegistry.Object,
        Options.Create(new AudiobookManagerSettings
        {
            UpcomingReleasesCheckEnabled = checkEnabled,
            UpcomingReleasesCheckIntervalHours = 24,
        }),
        _logger.Object);

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

    [TestMethod]
    public async Task ExecuteAsync_CheckDisabled_NeverPublishesStatus()
    {
        var worker = MakeWorker(checkEnabled: false);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(CancellationToken.None);

        _statusRegistry.Verify(r => r.SetRunning(It.IsAny<string>()), Times.Never);
        _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Never);
    }

    // Shares UpcomingReleasesController.RefreshGate with the manual "Check Now" endpoint - a tick
    // landing while that gate is already held must skip without touching the status registry
    // (the holder of the gate owns the status for that run).
    [TestMethod]
    public async Task ExecuteAsync_GateAlreadyHeld_SkipsWithoutPublishingStatus()
    {
        RefreshGate.Wait(0);
        try
        {
            var worker = MakeWorker();

            await worker.StartAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            await worker.StopAsync(CancellationToken.None);

            _statusRegistry.Verify(r => r.SetRunning(It.IsAny<string>()), Times.Never);
            _upcomingReleaseService.Verify(s => s.RefreshUpcomingReleasesAsync(), Times.Never);
        }
        finally
        {
            RefreshGate.Release();
        }
    }
}
