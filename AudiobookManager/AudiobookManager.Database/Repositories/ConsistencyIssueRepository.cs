using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class ConsistencyIssueRepository : IConsistencyIssueRepository
{
    private readonly DatabaseContext _db;

    public ConsistencyIssueRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<List<ConsistencyIssue>> GetAllWithAudiobookAsync()
    {
        // Read-only: nothing mutates these, and tracking every issue plus its audiobook and
        // author graph for the lifetime of the request is pure overhead on a large library.
        return await _db.ConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.IssueType)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    public async Task<ConsistencyIssue?> GetByIdAsync(long id)
    {
        return await _db.ConsistencyIssues
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .FirstOrDefaultAsync(ci => ci.Id == id);
    }

    public async Task InsertAsync(ConsistencyIssue issue)
    {
        _db.Add(issue);
        await _db.SaveChangesAsync();
    }

    public async Task InsertRangeAsync(IEnumerable<ConsistencyIssue> issues)
    {
        var issueList = issues as ICollection<ConsistencyIssue> ?? issues.ToList();
        if (issueList.Count == 0)
        {
            return;
        }

        _db.AddRange(issueList);
        await _db.SaveChangesAsync();
    }

    public async Task ClearAllAsync()
    {
        // One DELETE statement. RemoveRange over the DbSet would fetch and track every row just
        // to issue an individual DELETE per id. ExecuteDeleteAsync bypasses the change tracker,
        // so drop any rows this context still holds - SQLite reuses deleted rowids, and a stale
        // tracked entity would shadow the new row that inherits its id.
        await _db.ConsistencyIssues.ExecuteDeleteAsync();
        DetachTracked();
    }

    public async Task DeleteAsync(long id)
    {
        var entity = await _db.ConsistencyIssues.FindAsync(id);
        if (entity != null)
        {
            _db.ConsistencyIssues.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteByAudiobookIdAsync(long audiobookId)
    {
        await _db.ConsistencyIssues
            .Where(ci => ci.AudiobookId == audiobookId)
            .ExecuteDeleteAsync();
        DetachTracked(ci => ci.AudiobookId == audiobookId);
    }

    public async Task DeleteByAudiobookIdAndTypesAsync(long audiobookId, IEnumerable<ConsistencyIssueType> types)
    {
        var typeList = types.ToList();
        await _db.ConsistencyIssues
            .Where(ci => ci.AudiobookId == audiobookId && typeList.Contains(ci.IssueType))
            .ExecuteDeleteAsync();
        DetachTracked(ci => ci.AudiobookId == audiobookId && typeList.Contains(ci.IssueType));
    }

    public async Task<List<ConsistencyIssue>> GetByTypeAsync(ConsistencyIssueType issueType)
    {
        return await _db.ConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .Where(ci => ci.IssueType == issueType)
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<long, int>> GetIssueSummaryAsync()
    {
        return await _db.ConsistencyIssues
            .GroupBy(ci => ci.AudiobookId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<List<ConsistencyIssue>> GetByAudiobookIdAsync(long audiobookId)
    {
        return await _db.ConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .Where(ci => ci.AudiobookId == audiobookId)
            .OrderBy(ci => ci.IssueType)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    private void DetachTracked(Func<ConsistencyIssue, bool>? predicate = null)
    {
        foreach (var entry in _db.ChangeTracker.Entries<ConsistencyIssue>()
                     .Where(e => predicate is null || predicate(e.Entity))
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
