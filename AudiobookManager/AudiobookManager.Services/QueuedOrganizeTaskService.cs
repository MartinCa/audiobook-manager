using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services.MappingExtensions;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;
public class QueuedOrganizeTaskService : IQueuedOrganizeTaskService
{
    private readonly ILogger<QueuedOrganizeTaskService> _logger;
    private readonly IQueuedOrganizeTaskRepository _repository;

    public QueuedOrganizeTaskService(IQueuedOrganizeTaskRepository repository, ILogger<QueuedOrganizeTaskService> logger)
    {
        _repository = repository;
        _logger = logger;
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

        _logger.LogInformation("({audiobookFile}) Queued organize task", audiobook.FileInfo.FullPath);
        return dbEntity.ToDomain();
    }
}
