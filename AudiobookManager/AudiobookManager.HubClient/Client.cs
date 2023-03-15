﻿using AudiobookManager.Api.Async;
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
        _hubConnection.On<QueueError>("QueueError", QueueError);

        _hubConnection.StartAsync();
    }

    public Task QueueError(QueueError queueError)
    {
        Console.WriteLine($"Error {queueError.OriginalFileLocation}: {queueError.Error}");
        return Task.CompletedTask;
    }

    public Task UpdateProgress(ProgressUpdate progressUpdate)
    {
        Console.WriteLine($"{progressUpdate.OriginalFileLocation}, {progressUpdate.ProgressMessage}, {progressUpdate.Progress}");
        return Task.CompletedTask;
    }
}
