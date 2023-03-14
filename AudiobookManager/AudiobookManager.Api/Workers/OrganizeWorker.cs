using AudiobookManager.Api.Async;
using AudiobookManager.Services;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Workers;

public class OrganizeWorker : BackgroundService
{
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceProvider _serviceProvider;

    public OrganizeWorker(IHubContext<OrganizeHub, IOrganize> organizeHub, IServiceProvider serviceProvider)
    {
        _organizeHub = organizeHub;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var organizeTaskService = scope.ServiceProvider.GetRequiredService<IQueuedOrganizeTaskService>();
                var task = await organizeTaskService.GetNextQueuedOrganizeTask();
                if (task == null)
                {
                    Thread.Sleep(5000);
                    continue;
                }

                var audiobookService = scope.ServiceProvider.GetRequiredService<IAudiobookService>();

                await audiobookService.OrganizeAudiobook(task.Audiobook, (msg, prg) => UpdateProgress(task.OriginalFileLocation, msg, prg));

                await organizeTaskService.DeleteQueuedOrganizeTask(task.OriginalFileLocation);
            }
        }
    }

    private async Task UpdateProgress(string originalFileLocation, string progressMessage, int progress)
    {
        await _organizeHub.Clients.All.UpdateProgress(new ProgressUpdate(originalFileLocation, progressMessage, progress));
    }
}
