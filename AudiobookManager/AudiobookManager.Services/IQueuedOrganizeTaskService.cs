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
    /// Gives the row at <paramref name="originalFileLocation"/> exactly one more attempt - the
    /// worker will pick it up again, but a single further failure dead-letters it immediately
    /// rather than requiring several. Returns false if no row exists at that path.
    /// </summary>
    public Task<bool> RetryQueuedOrganizeTask(string originalFileLocation);
}
