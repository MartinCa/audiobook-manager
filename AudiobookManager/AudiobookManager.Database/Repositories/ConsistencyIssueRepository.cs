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
        return await _db.ConsistencyIssues
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.IssueType)
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

    public async Task ClearAllAsync()
    {
        _db.ConsistencyIssues.RemoveRange(_db.ConsistencyIssues);
        await _db.SaveChangesAsync();
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
        var issues = await _db.ConsistencyIssues
            .Where(ci => ci.AudiobookId == audiobookId)
            .ToListAsync();
        _db.ConsistencyIssues.RemoveRange(issues);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteByAudiobookIdAndTypesAsync(long audiobookId, IEnumerable<ConsistencyIssueType> types)
    {
        var issues = await _db.ConsistencyIssues
            .Where(ci => ci.AudiobookId == audiobookId && types.Contains(ci.IssueType))
            .ToListAsync();
        _db.ConsistencyIssues.RemoveRange(issues);
        await _db.SaveChangesAsync();
    }

    public async Task<List<ConsistencyIssue>> GetByTypeAsync(ConsistencyIssueType issueType)
    {
        return await _db.ConsistencyIssues
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .Where(ci => ci.IssueType == issueType)
            .OrderBy(ci => ci.AudiobookId)
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
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .Where(ci => ci.AudiobookId == audiobookId)
            .OrderBy(ci => ci.IssueType)
            .ToListAsync();
    }
}
