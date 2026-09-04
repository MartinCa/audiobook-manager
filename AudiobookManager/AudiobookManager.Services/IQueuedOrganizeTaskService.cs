using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;
public interface IQueuedOrganizeTaskService
{
    public Task<QueuedOrganizeTask> QueueOrganizeTask(Audiobook audiobook);
    public Task DeleteQueuedOrganizeTask(string originalFileLocation);
    public Task<IList<QueuedOrganizeTask>> GetQueuedOrganizeTasks();
    public Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask();
    public Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation);

    /// <summary>Every row that has failed to deserialize at least once, most recently failed first.</summary>
    public Task<IList<FailedOrganizeTaskRow>> GetFailedQueuedOrganizeTasks();

    /// <summary>
    /// Clears the failure state on the row at <paramref name="originalFileLocation"/> so the
    /// worker will pick it up again, regardless of how many times it had previously failed.
    /// Returns false if no row exists at that path.
    /// </summary>
    public Task<bool> RetryQueuedOrganizeTask(string originalFileLocation);
}
