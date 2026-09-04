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
        var tasks = new List<QueuedOrganizeTask>(dbEntities.Count);

        foreach (var dbEntity in dbEntities)
        {
            try
            {
                tasks.Add(dbEntity.ToDomain());
            }
            catch (Exception ex)
            {
                // A row that fails to deserialize (see GetNextQueuedOrganizeTask/
                // QueuedOrganizeTaskDeserializationException) must not break this listing for
                // every other queued book. GET /api/queue/books is polled by BookList.tsx purely
                // to show "queued" badges, and one bad row here previously took the whole
                // response down - silently, since BookList swallows the failure into an empty
                // list, which made every book in the library lose its badge instead of just the
                // one that's actually stuck.
                //
                // Skipped rather than surfaced: this method has no way to represent an unreadable
                // row as a QueuedOrganizeTask (constructing one requires a deserialized
                // Audiobook), and giving users visibility into - and a way to clear - failed/
                // dead-lettered rows is a separate piece of work (#1322 follow-up). Not counted
                // as a failure via RecordDeserializationFailureAsync either: that would tie the
                // dead-letter threshold to how often this list is polled rather than to actual
                // worker attempts.
                _logger.LogWarning(
                    ex,
                    "Skipping queued organize task at '{OriginalFileLocation}' from the queue list: json_audiobook could not be deserialized",
                    dbEntity.OriginalFileLocation);
            }
        }

        return tasks;
    }

    public async Task<IList<FailedOrganizeTaskRow>> GetFailedQueuedOrganizeTasks()
    {
        return await _repository.GetFailedQueuedOrganizeTasksAsync();
    }

    public async Task<bool> RetryQueuedOrganizeTask(string originalFileLocation)
    {
        return await _repository.RetryQueuedOrganizeTaskAsync(originalFileLocation);
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
