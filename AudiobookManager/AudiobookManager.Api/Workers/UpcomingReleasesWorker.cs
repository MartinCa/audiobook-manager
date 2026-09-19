using System.Diagnostics;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Services;
using Cronos;

namespace AudiobookManager.Api.Workers;

/// <summary>
/// Periodically polls every followed-and-matched author/series for upcoming releases, on a
/// standard 5-field cron schedule (<see cref="Domain.LibrarySettings.UpcomingReleasesCronSchedule"/>)
/// read live from the database each iteration, so a change made on the Settings page takes effect
/// without an app restart. Unlike <see cref="OrganizeWorker"/>'s queue poll, there is no work item
/// to fail on individually - <see cref="IUpcomingReleaseService.RefreshUpcomingReleasesAsync"/>
/// already tolerates a single author/series failing without aborting the sweep - so this loop only
/// needs to survive a whole sweep throwing, which it does the same way OrganizeWorker survives a
/// bad queue read: log and wait for the next tick rather than crash the host.
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
    /// <summary>
    /// Default for <see cref="_disabledPollInterval"/>: how long a disabled/unparsable-cron check
    /// waits before re-checking the stored settings. Short enough that re-enabling the check (or
    /// fixing a hand-edited bad cron) takes effect promptly without an app restart; long enough
    /// not to hammer the database in a tight loop.
    /// </summary>
    private static readonly TimeSpan DefaultDisabledPollInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly ILogger<UpcomingReleasesWorker> _logger;
    private readonly TimeSpan _disabledPollInterval;

    public UpcomingReleasesWorker(
        IServiceProvider serviceProvider,
        IOperationStatusRegistry statusRegistry,
        ILogger<UpcomingReleasesWorker> logger,
        TimeSpan? disabledPollInterval = null)
    {
        _serviceProvider = serviceProvider;
        _statusRegistry = statusRegistry;
        _logger = logger;
        // Overridable only so tests can shrink the disabled/bad-cron re-check wait below a real
        // 5 minutes; production always gets the default via DI (nothing registers a TimeSpan).
        _disabledPollInterval = disabledPollInterval ?? DefaultDisabledPollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            var firstIteration = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                var (enabled, cronSchedule) = await ReadScheduleAsync();
                var now = DateTimeOffset.UtcNow;
                var nextOccurrence = ComputeNextRun(enabled, cronSchedule, now, _logger);

                if (nextOccurrence is null)
                {
                    // Disabled, or an unparsable cron (shouldn't happen given
                    // SettingsController's write-side validation, but the stored value could
                    // predate that validation or have been hand-edited) - either way, wait and
                    // re-check rather than crash the worker or loop tightly.
                    await Task.Delay(_disabledPollInterval, stoppingToken);
                    continue;
                }

                if (firstIteration)
                {
                    // Run once immediately on startup - a fresh follow shouldn't have to wait for
                    // the first cron occurrence - then on the schedule from there.
                    firstIteration = false;
                    await RunOnceAsync(stoppingToken);
                    continue;
                }

                var delay = nextOccurrence.Value - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cooperative shutdown.
        }
    }

    /// <summary>
    /// The pure scheduling decision, pulled out of <see cref="ExecuteAsync"/> so it is unit
    /// testable without waiting on a real cron tick: null when disabled or the cron fails to
    /// parse, otherwise the next UTC occurrence strictly after <paramref name="now"/>.
    /// </summary>
    public static DateTimeOffset? ComputeNextRun(bool enabled, string cronSchedule, DateTimeOffset now, ILogger? logger = null)
    {
        if (!enabled)
        {
            return null;
        }

        if (!CronExpression.TryParse(cronSchedule, CronFormat.Standard, out var cron))
        {
            logger?.LogWarning(
                "Upcoming releases cron schedule '{CronSchedule}' is not a valid standard 5-field cron expression; skipping until the setting is fixed",
                cronSchedule);
            return null;
        }

        return cron!.GetNextOccurrence(now, TimeZoneInfo.Utc);
    }

    private async Task<(bool Enabled, string CronSchedule)> ReadScheduleAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var settings = await settingsService.GetLibrarySettings();
        return (settings.UpcomingReleasesEnabled, settings.UpcomingReleasesCronSchedule);
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

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string? error = null;

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
            error = ex.Message;
            _logger.LogError(ex, "Error refreshing upcoming releases");
        }
        finally
        {
            stopwatch.Stop();
            _statusRegistry.SetFinished(UpcomingReleasesController.RefreshOperationKey);
            UpcomingReleasesController.RefreshGate.Release();

            // Record the run even on a cooperative shutdown mid-sweep - "Failed", not silently
            // absent - but only if this call didn't itself lose its own scope; a fresh scope is
            // used deliberately since the one above may already be disposed.
            using var recordScope = _serviceProvider.CreateScope();
            var scheduledTaskService = recordScope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
            await scheduledTaskService.RecordTaskRunAsync(
                ScheduledTaskKeys.UpcomingReleasesRefresh, startedAt, stopwatch.Elapsed, error is null, error);
        }
    }
}
