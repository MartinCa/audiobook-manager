using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Api.Workers;

/// <summary>
/// Periodically polls every followed-and-matched author/series for upcoming releases. Unlike
/// <see cref="OrganizeWorker"/>'s queue poll, there is no work item to fail on individually -
/// <see cref="IUpcomingReleaseService.RefreshUpcomingReleasesAsync"/> already tolerates a single
/// author/series failing without aborting the sweep - so this loop only needs to survive a whole
/// sweep throwing, which it does the same way OrganizeWorker survives a bad queue read: log and
/// wait for the next tick rather than crash the host.
///
/// Each tick takes <see cref="UpcomingReleasesController.RefreshGate"/> - the same gate the
/// manual "Check Now" refresh endpoint uses - non-blocking: a tick landing while a manual refresh
/// is already running skips rather than queuing behind it, so the two never run the sweep back to
/// back and double the Hardcover request spend for the same discoveries.
///
/// Publishes to the same <see cref="IOperationStatusRegistry"/> key
/// (<see cref="UpcomingReleasesController.RefreshOperationKey"/>) the manual "Check Now" endpoint
/// uses, so the frontend's poll of <c>GET api/operations/{key}/status</c> reflects a scheduled
/// sweep too - without this, a tick landing while the page happened to be open would run for
/// minutes with the "Check Now" button showing idle the whole time.
/// </summary>
public class UpcomingReleasesWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<UpcomingReleasesWorker> _logger;

    public UpcomingReleasesWorker(
        IServiceProvider serviceProvider,
        IOperationStatusRegistry statusRegistry,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<UpcomingReleasesWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _statusRegistry = statusRegistry;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.UpcomingReleasesCheckEnabled)
        {
            return;
        }

        await Task.Yield();

        var interval = TimeSpan.FromHours(Math.Max(1, _settings.UpcomingReleasesCheckIntervalHours));
        using var timer = new PeriodicTimer(interval);

        try
        {
            // Run once immediately on startup - a fresh follow shouldn't have to wait a full
            // interval for its first poll - then on the timer from there. Both calls share the
            // same try/catch below: RunOnceAsync rethrows OperationCanceledException on a
            // cooperative shutdown, and the startup call must not let that escape ExecuteAsync
            // uncaught (BackgroundService logs an escaping exception as a hosted-service
            // failure, even for an expected shutdown).
            await RunOnceAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cooperative shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        // Non-blocking: a tick landing while the manual "Check Now" refresh is already running
        // (or vice versa) skips rather than queuing behind it - see the gate's own doc comment.
        if (!UpcomingReleasesController.RefreshGate.Wait(0))
        {
            _logger.LogInformation("Skipping this upcoming-releases tick: a refresh is already running");
            return;
        }

        _statusRegistry.SetRunning(UpcomingReleasesController.RefreshOperationKey);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var upcomingReleaseService = scope.ServiceProvider.GetRequiredService<IUpcomingReleaseService>();
            await upcomingReleaseService.RefreshUpcomingReleasesAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing upcoming releases");
        }
        finally
        {
            _statusRegistry.SetFinished(UpcomingReleasesController.RefreshOperationKey);
            UpcomingReleasesController.RefreshGate.Release();
        }
    }
}
