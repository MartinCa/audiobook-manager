using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services.MappingExtensions;

namespace AudiobookManager.Services;
public class QueuedOrganizeTaskService : IQueuedOrganizeTaskService
{
    private readonly IQueuedOrganizeTaskRepository _repository;

    public QueuedOrganizeTaskService(IQueuedOrganizeTaskRepository repository)
    {
        _repository = repository;
    }

    public async Task DeleteQueuedOrganizeTask(string originalFileLocation)
    {
        await _repository.DeleteQueuedOrganizeTask(originalFileLocation);
    }

    public async Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask()
    {
        var dbEntity = await _repository.GetNextQueuedOrganizeTask();
        return dbEntity?.ToDomain();
    }

    public async Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation)
    {
        var dbEntity = await _repository.GetQueuedOrganizeTask(originalFileLocation);
        return dbEntity?.ToDomain();
    }

    public async Task<IList<QueuedOrganizeTask>> GetQueuedOrganizeTasks()
    {
        var dbEntities = await _repository.GetAllQueuedOrganizeTasks();
        return dbEntities.Select(x => x.ToDomain()).ToList();
    }

    public async Task<QueuedOrganizeTask> QueueOrganizeTask(Audiobook audiobook)
    {
        var domainModel = new QueuedOrganizeTask(audiobook.FileInfo.FullPath, audiobook, DateTime.UtcNow);
        var dbEntity = domainModel.ToDb();
        dbEntity = await _repository.InsertQueuedOrganizeTask(dbEntity);
        return dbEntity.ToDomain();
    }
}
