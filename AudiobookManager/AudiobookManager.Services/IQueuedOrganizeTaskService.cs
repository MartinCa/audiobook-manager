using AudiobookManager.Domain;

namespace AudiobookManager.Services;
public interface IQueuedOrganizeTaskService
{
    public Task<QueuedOrganizeTask> QueueOrganizeTask(Audiobook audiobook);
    public Task DeleteQueuedOrganizeTask(string originalFileLocation);
    public Task<IList<QueuedOrganizeTask>> GetQueuedOrganizeTasks();
    public Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask();
    public Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation);
}
