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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                QueuedOrganizeTask? task = null;
                try
                {
                    var organizeTaskService = scope.ServiceProvider.GetRequiredService<IQueuedOrganizeTaskService>();
                    task = await organizeTaskService.GetNextQueuedOrganizeTask();
                    if (task == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
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
                } catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing organize task {OriginalFileLoation}", task?.OriginalFileLocation ?? "");

                    if (task != null)
                    {
                        await QueueError(task.OriginalFileLocation, ex.Message);
                        var organizeTaskService = scope.ServiceProvider.GetRequiredService<IQueuedOrganizeTaskService>();
                        await organizeTaskService.DeleteQueuedOrganizeTask(task.OriginalFileLocation);
                    }
                }
            }
        }
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
