using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class QueuedOrganizeTaskRepository : IQueuedOrganizeTaskRepository
{
    private readonly DatabaseContext _db;

    public QueuedOrganizeTaskRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task DeleteQueuedOrganizeTask(string originalFileLocation)
    {
        await _db.QueuedOrganizeTasks.Where(x => x.OriginalFileLocation == originalFileLocation).ExecuteDeleteAsync();
    }

    public async Task<IList<QueuedOrganizeTask>> GetAllQueuedOrganizeTasks()
    {
        return await _db.QueuedOrganizeTasks.ToListAsync();
    }

    public async Task<QueuedOrganizeTask?> GetNextQueuedOrganizeTask()
    {
        return await _db.QueuedOrganizeTasks.OrderBy(x => x.QueuedTime).FirstOrDefaultAsync();
    }

    public async Task<QueuedOrganizeTask?> GetQueuedOrganizeTask(string originalFileLocation)
    {
        return await _db.QueuedOrganizeTasks.FindAsync(originalFileLocation);
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
