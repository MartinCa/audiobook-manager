using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services.MappingExtensions;
using Microsoft.EntityFrameworkCore;
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
        if (dbEntity is null)
        {
            return null;
        }

        try
        {
            return dbEntity.ToDomain();
        }
        catch (Exception ex)
        {
            // The row is left in place (see QueuedOrganizeTaskDeserializationException) - only its
            // failure count is bumped, so the repository can stop serving it once that count
            // crosses the dead-letter threshold instead of leaving it permanently first-in-line.
            await _repository.RecordDeserializationFailureAsync(dbEntity.OriginalFileLocation, ex.Message);
            throw new QueuedOrganizeTaskDeserializationException(dbEntity.OriginalFileLocation, ex);
        }
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
        var originalFileLocation = audiobook.FileInfo.FullPath;

        // Checked before inserting so the common case - the user clicking Organize a second time -
        // is answered as a conflict rather than as a constraint violation out of the repository.
        if (await _repository.GetQueuedOrganizeTask(originalFileLocation) is not null)
        {
            throw new OrganizeTaskAlreadyQueuedException(originalFileLocation);
        }

        var domainModel = new QueuedOrganizeTask(originalFileLocation, audiobook, DateTime.UtcNow);
        var dbEntity = domainModel.ToDb();

        try
        {
            dbEntity = await _repository.InsertQueuedOrganizeTask(dbEntity);
        }
        catch (DbUpdateException)
        {
            // The check above spans an await, so two requests for the same file can both pass it.
            // The loser lands here; re-reading is what distinguishes "someone else queued it first"
            // from any other save failure, which is not ours to swallow.
            if (await _repository.GetQueuedOrganizeTask(originalFileLocation) is not null)
            {
                throw new OrganizeTaskAlreadyQueuedException(originalFileLocation);
            }

            throw;
        }

        _logger.LogInformation("({audiobookFile}) Queued organize task", originalFileLocation);
        return dbEntity.ToDomain();
    }
}
