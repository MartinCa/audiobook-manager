using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IQueuedOrganizeTaskRepository
{
    public Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation);
    public Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask();
    public Task<IList<QueuedOrganizeTask>> GetAllQueuedOrganizeTasks();
    public Task<QueuedOrganizeTask> InsertQueuedOrganizeTask(QueuedOrganizeTask task);
    public Task DeleteQueuedOrganizeTask(string originalFileLocation);
}
