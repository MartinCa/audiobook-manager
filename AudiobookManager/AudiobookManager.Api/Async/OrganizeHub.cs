using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Async;

public class OrganizeHub : Hub<IOrganize>
{
    public async Task SendProgressUpdateToClients(string originalLocation, string progressMessage, int progress)
    {
        await Clients.All.UpdateProgress(new ProgressUpdate(originalLocation, progressMessage, progress));
    }
}
