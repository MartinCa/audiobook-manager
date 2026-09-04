using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IQueuedOrganizeTaskRepository
{
    public Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation);
    public Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask();
    public Task<IList<QueuedOrganizeTask>> GetAllQueuedOrganizeTasks();
    public Task<QueuedOrganizeTask> InsertQueuedOrganizeTask(QueuedOrganizeTask task);
    public Task DeleteQueuedOrganizeTask(string originalFileLocation);

    /// <summary>
    /// Records that the row at <paramref name="originalFileLocation"/> could not be turned into a
    /// domain task, incrementing its failure count. Once the count reaches the repository's
    /// threshold, <see cref="GetNextQueuedOrganizeTask"/> stops returning the row, so a
    /// permanently-bad row cannot block every row queued behind it.
    /// </summary>
    public Task RecordDeserializationFailureAsync(string originalFileLocation, string reason);
}
