using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AudiobookManager.Api.Async;
using Microsoft.AspNetCore.SignalR.Client;

namespace AudiobookManager.HubClient;
public class Client : IOrganize
{
    private HubConnection _hubConnection;

    public Client()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5271/hubs/organize")
            .Build();

        _hubConnection.On<ProgressUpdate>("UpdateProgress", UpdateProgress);

        _hubConnection.StartAsync();
    }

    public Task UpdateProgress(ProgressUpdate progressUpdate)
    {
        Console.WriteLine($"{progressUpdate.OriginalFileLocation}, {progressUpdate.ProgressMessage}, {progressUpdate.Progress}");
        return Task.CompletedTask;
    }
}
