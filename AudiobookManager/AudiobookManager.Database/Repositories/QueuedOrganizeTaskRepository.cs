using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class QueuedOrganizeTaskRepository : IQueuedOrganizeTaskRepository
{
    // A row this many deserialization failures deep is treated as dead: GetNextQueuedOrganizeTask
    // stops returning it, so the queue can move on to rows behind it. A few retries first (rather
    // than dead-lettering on the very first failure) give a transient cause - e.g. a concurrent
    // write catching the row mid-save - a chance to clear on its own.
    private const int MaxDeserializationFailures = 5;

    private readonly DatabaseContext _db;

    public QueuedOrganizeTaskRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task DeleteQueuedOrganizeTask(string originalFileLocation)
    {
        await _db.QueuedOrganizeTasks.Where(x => x.OriginalFileLocation == originalFileLocation).ExecuteDeleteAsync();

        // ExecuteDeleteAsync issues the DELETE straight to the database and tells the change
        // tracker nothing, so a context that had this task tracked (from having inserted or read
        // it) goes on holding a row that no longer exists. Re-adding the same path in that context
        // then fails with an identity conflict rather than queueing. Detaching is what makes the
        // delete mean the same thing to the tracker as it does to the database.
        var tracked = _db.ChangeTracker
            .Entries<QueuedOrganizeTask>()
            .FirstOrDefault(entry => entry.Entity.OriginalFileLocation == originalFileLocation);

        if (tracked is not null)
        {
            tracked.State = EntityState.Detached;
        }
    }

    public async Task<IList<QueuedOrganizeTask>> GetAllQueuedOrganizeTasks()
    {
        return await _db.QueuedOrganizeTasks.ToListAsync();
    }

    public async Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask()
    {
        return await _db.QueuedOrganizeTasks
            .Where(x => x.FailureCount < MaxDeserializationFailures)
            .OrderBy(x => x.QueuedTime)
            .FirstOrDefaultAsync();
    }

    public async Task RecordDeserializationFailureAsync(string originalFileLocation, string reason)
    {
        var failedAt = DateTime.UtcNow;

        // ExecuteUpdateAsync, for the same reason DeleteQueuedOrganizeTask uses ExecuteDeleteAsync
        // above: it writes straight to the database without needing the row tracked first, and the
        // increment is expressed against the column's current value rather than a value read
        // earlier and possibly stale.
        await _db.QueuedOrganizeTasks
            .Where(x => x.OriginalFileLocation == originalFileLocation)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.FailureCount, x => x.FailureCount + 1)
                .SetProperty(x => x.LastFailureReason, reason)
                .SetProperty(x => x.LastFailureAt, failedAt));
    }

    public async Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation)
    {
        // A query rather than FindAsync, and untracked. FindAsync answers from the change
        // tracker's identity map before it reaches the database, and DeleteQueuedOrganizeTask
        // above uses ExecuteDeleteAsync, which deletes the row without telling the tracker - so
        // after a delete, FindAsync went on returning the removed task for the lifetime of the
        // context. Nothing needs the tracked instance: every caller maps the result straight to
        // the domain model.
        return await _db.QueuedOrganizeTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OriginalFileLocation == originalFileLocation);
    }

    public async Task<QueuedOrganizeTask> InsertQueuedOrganizeTask(QueuedOrganizeTask task)
    {
        if (task.QueuedTime == default)
        {
            task.QueuedTime = DateTime.UtcNow;
        }

        _db.QueuedOrganizeTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }
}
