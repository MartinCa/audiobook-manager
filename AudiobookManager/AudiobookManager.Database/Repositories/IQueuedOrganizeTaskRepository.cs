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

    /// <summary>
    /// Every row that has failed to deserialize at least once, most recently failed first.
    /// Projected without <c>JsonAudiobook</c> - the column that fails is exactly what a caller
    /// must not attempt to read back here.
    /// </summary>
    public Task<IList<FailedOrganizeTaskRow>> GetFailedQueuedOrganizeTasksAsync();

    /// <summary>
    /// Clears the failure count/reason/timestamp on the row at <paramref name="originalFileLocation"/>,
    /// making it eligible for <see cref="GetNextQueuedOrganizeTask"/> again regardless of how many
    /// times it had previously failed. Returns false if no row exists at that path.
    /// </summary>
    public Task<bool> RetryQueuedOrganizeTaskAsync(string originalFileLocation);
}
