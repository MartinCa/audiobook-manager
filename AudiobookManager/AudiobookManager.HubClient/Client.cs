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

    public Task LibraryScanProgress(LibraryScanProgress progress)
    {
        Console.WriteLine($"Library scan: {progress.Message} ({progress.FilesScanned}/{progress.TotalFiles})");
        return Task.CompletedTask;
    }

    public Task LibraryScanComplete(LibraryScanComplete result)
    {
        Console.WriteLine($"Library scan complete: {result.TotalFilesScanned} total, {result.NewFilesDiscovered} new, {result.AlreadyTracked} tracked");
        return Task.CompletedTask;
    }

    public Task ConsistencyCheckProgress(ConsistencyCheckProgress progress)
    {
        Console.WriteLine($"Consistency check: {progress.Message} ({progress.BooksChecked}/{progress.TotalBooks}), issues: {progress.IssuesFound}");
        return Task.CompletedTask;
    }

    public Task ConsistencyCheckComplete(ConsistencyCheckComplete result)
    {
        Console.WriteLine($"Consistency check complete: {result.TotalBooksChecked} books, {result.TotalIssuesFound} issues");
        return Task.CompletedTask;
    }
}
