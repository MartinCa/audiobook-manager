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
    /// Default for <see cref="_settingsPollInterval"/>. Originally just the disabled/unparsable-cron
    /// re-check wait, this now also caps how long the loop ever sleeps in one slice while waiting
    /// for the next scheduled run (see <see cref="ExecuteAsync"/>) - both are the same underlying
    /// question ("how stale is our view of the stored settings allowed to get"), so one constant
    /// serves both rather than introducing a second, unrelated magic number. Short enough that a
    /// saved change (re-enabling, fixing a hand-edited bad cron, or a new schedule entirely) takes
    /// effect within minutes rather than up to a full cron period; long enough not to hammer the
    /// database in a tight loop.
    /// </summary>
    private static readonly TimeSpan DefaultSettingsPollInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly ILogger<UpcomingReleasesWorker> _logger;
    private readonly TimeSpan _settingsPollInterval;

    public UpcomingReleasesWorker(
        IServiceProvider serviceProvider,
        IOperationStatusRegistry statusRegistry,
        ILogger<UpcomingReleasesWorker> logger,
        TimeSpan? settingsPollInterval = null)
    {
        _serviceProvider = serviceProvider;
        _statusRegistry = statusRegistry;
        _logger = logger;
        // Overridable only so tests can shrink the settings poll interval below a real 5 minutes;
        // production always gets the default via DI (nothing registers a TimeSpan).
        _settingsPollInterval = settingsPollInterval ?? DefaultSettingsPollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            var firstIteration = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                (bool Enabled, string CronSchedule) schedule;
                try
                {
                    schedule = await ReadScheduleAsync();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A transient DB error (e.g. SQLite busy under a concurrent scan) reading the
                    // schedule must not escape the loop - BackgroundService faults the whole host
                    // on an unhandled exception (default IHostOptions.BackgroundServiceExceptionBehavior
                    // is StopHost). Log and retry on the same poll interval used for the
                    // disabled/unparsable-cron case, rather than crash the host over a read that
                    // will very likely succeed next time.
                    _logger.LogWarning(
                        ex,
                        "Error reading the upcoming-releases schedule; retrying in {PollInterval}",
                        _settingsPollInterval);
                    await Task.Delay(_settingsPollInterval, stoppingToken);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var nextOccurrence = ComputeNextRun(schedule.Enabled, schedule.CronSchedule, now, _logger);

                if (nextOccurrence is null)
                {
                    // Disabled, or an unparsable cron (shouldn't happen given
                    // SettingsController's write-side validation, but the stored value could
                    // predate that validation or have been hand-edited) - either way, wait and
                    // re-check rather than crash the worker or loop tightly.
                    await Task.Delay(_settingsPollInterval, stoppingToken);
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

                var remaining = nextOccurrence.Value - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    await RunOnceAsync(stoppingToken);
                    continue;
                }

                // Sleep in slices no longer than the settings poll interval rather than one long
                // Task.Delay all the way to the next occurrence, so a schedule/enabled change (or
                // a fixed bad cron) saved from the Settings page is picked up - and the wait
                // re-derived against it - within minutes instead of up to a whole cron period
                // (e.g. ~24h for the daily default). The loop re-reads the schedule from the top
                // on every slice; once the current occurrence is actually reached, the branch
                // above runs it.
                var sleepFor = remaining < _settingsPollInterval ? remaining : _settingsPollInterval;
                await Task.Delay(sleepFor, stoppingToken);
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
            // Cooperative shutdown mid-sweep is not a successful run - record it as failed before
            // rethrowing so ExecuteAsync still sees the cancellation and shuts down cleanly, while
            // the Tasks page doesn't show a shutdown-interrupted sweep as "Success".
            error = "Cancelled (application shutting down)";
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
            // used deliberately since the one above may already be disposed. The record-write
            // itself is swallowed on failure (logged, not rethrown/masking the original sweep
            // exception or the pending OperationCanceledException) - a transient DB error writing
            // bookkeeping must never crash the host or replace what actually happened.
            try
            {
                using var recordScope = _serviceProvider.CreateScope();
                var scheduledTaskService = recordScope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
                await scheduledTaskService.RecordTaskRunAsync(
                    ScheduledTaskKeys.UpcomingReleasesRefresh, startedAt, stopwatch.Elapsed, error is null, error);
            }
            catch (Exception recordEx)
            {
                _logger.LogError(recordEx, "Error recording upcoming-releases task run");
            }
        }
    }
}
