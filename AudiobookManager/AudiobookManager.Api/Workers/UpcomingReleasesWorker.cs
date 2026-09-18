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
/// </summary>
public class UpcomingReleasesWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<UpcomingReleasesWorker> _logger;

    public UpcomingReleasesWorker(
        IServiceProvider serviceProvider,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<UpcomingReleasesWorker> logger)
    {
        _serviceProvider = serviceProvider;
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

        // Run once immediately on startup - a fresh follow shouldn't have to wait a full
        // interval for its first poll - then on the timer from there.
        await RunOnceAsync(stoppingToken);

        try
        {
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
        using var scope = _serviceProvider.CreateScope();
        try
        {
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
    }
}
