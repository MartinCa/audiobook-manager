using AudiobookManager.Api.Async;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Workers;

public class OrganizeWorker : BackgroundService
{
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrganizeWorker> _logger;

    public OrganizeWorker(IHubContext<OrganizeHub, IOrganize> organizeHub, IServiceProvider serviceProvider, ILogger<OrganizeWorker> logger)
    {
        _organizeHub = organizeHub;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>How long the loop sleeps when the queue is empty.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The first delay after a failure, doubled on each consecutive one up to
    /// <see cref="MaxErrorDelay"/>.
    /// </summary>
    private static readonly TimeSpan InitialErrorDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxErrorDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        // The idle delay below covers only the "queue is empty" path. If the queue read itself
        // throws - SQLite busy under a concurrent scan, or a row whose json_audiobook no longer
        // deserialises - there is no task to fail and nothing to delete, so the loop used to come
        // straight back round at full speed: a pinned core and a log filling at thousands of lines
        // per second, for a bad row until someone removes it by hand.
        var consecutiveErrors = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var errorDelay = TimeSpan.Zero;

            using (var scope = _serviceProvider.CreateScope())
            {
                QueuedOrganizeTask? task = null;
                try
                {
                    var organizeTaskService = scope.ServiceProvider.GetRequiredService<IQueuedOrganizeTaskService>();
                    task = await organizeTaskService.GetNextQueuedOrganizeTask();
                    if (task == null)
                    {
                        consecutiveErrors = 0;
                        await Task.Delay(IdleDelay, stoppingToken);
                        continue;
                    }

                    var audiobookService = scope.ServiceProvider.GetRequiredService<IAudiobookService>();

                    await audiobookService.OrganizeAudiobook(task.Audiobook, (msg, prg) => UpdateProgress(task.OriginalFileLocation, msg, prg));

                    await organizeTaskService.DeleteQueuedOrganizeTask(task.OriginalFileLocation);

                    // Only untrack the discovered-books row once the organize actually succeeded, so a
                    // failure (e.g. a duplicate collision) leaves the row in place to retry or resolve
                    // instead of silently disappearing if the client missed the QueueError event.
                    var discoveredRepo = scope.ServiceProvider.GetRequiredService<IDiscoveredAudiobookRepository>();
                    await discoveredRepo.DeleteByPathAsync(task.OriginalFileLocation);

                    consecutiveErrors = 0;
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Cooperative shutdown: the idle Task.Delay above (or another await) was
                    // cancelled because the host is stopping. This is expected and not an error.
                    break;
                } catch (QueuedOrganizeTaskDeserializationException ex)
                {
                    // Unlike the generic catch below, this row is deliberately not deleted: its
                    // json_audiobook may be the only surviving copy of edits the user made before
                    // queuing. The service has already recorded the failure against the row, and
                    // once that crosses the repository's dead-letter threshold, GetNextQueuedOrganizeTask
                    // stops returning it - so the queue moves on without this row's only record
                    // being destroyed. See #1322.
                    _logger.LogError(ex, "Failed to deserialize queued organize task {OriginalFileLocation}", ex.OriginalFileLocation);
                    await QueueError(ex.OriginalFileLocation, ex.Message);

                    consecutiveErrors++;
                    errorDelay = BackoffFor(consecutiveErrors);

                    _logger.LogWarning(
                        "Organize worker backing off for {Delay} after {ConsecutiveErrors} consecutive failure(s)",
                        errorDelay,
                        consecutiveErrors);
                } catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing organize task {OriginalFileLoation}", task?.OriginalFileLocation ?? "");

                    if (task != null)
                    {
                        await QueueError(task.OriginalFileLocation, ex.Message);
                        var organizeTaskService = scope.ServiceProvider.GetRequiredService<IQueuedOrganizeTaskService>();
                        await organizeTaskService.DeleteQueuedOrganizeTask(task.OriginalFileLocation);
                    }

                    consecutiveErrors++;
                    errorDelay = BackoffFor(consecutiveErrors);

                    _logger.LogWarning(
                        "Organize worker backing off for {Delay} after {ConsecutiveErrors} consecutive failure(s)",
                        errorDelay,
                        consecutiveErrors);
                }
            }

            // Outside the scope, so the delay does not hold a DbContext (and everything else the
            // scope resolved) open for the length of the backoff.
            if (errorDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(errorDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Exponential, capped. Capped rather than unbounded because the condition usually clears on
    /// its own - a scan finishes, a mount comes back - and a worker that had backed off to hours
    /// would take just as long to notice.
    /// </summary>
    private static TimeSpan BackoffFor(int consecutiveErrors)
    {
        var scaled = InitialErrorDelay * Math.Pow(2, Math.Min(consecutiveErrors - 1, 10));
        return scaled < MaxErrorDelay ? scaled : MaxErrorDelay;
    }

    private async Task UpdateProgress(string originalFileLocation, string progressMessage, int progress)
    {
        await _organizeHub.Clients.All.UpdateProgress(new ProgressUpdate(originalFileLocation, progressMessage, progress));
    }

    private async Task QueueError(string originalFileLocation, string error)
    {
        await _organizeHub.Clients.All.QueueError(new QueueError(originalFileLocation, error));
    }
}
