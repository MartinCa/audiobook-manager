using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class BookConsistencyIssueRepository : IBookConsistencyIssueRepository
{
    private readonly DatabaseContext _db;

    public BookConsistencyIssueRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<List<BookConsistencyIssue>> GetAllWithAudiobookAsync()
    {
        // Read-only: nothing mutates these, and tracking every issue plus its audiobook and
        // author graph for the lifetime of the request is pure overhead on a large library.
        return await _db.BookConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.IssueType)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    public async Task<(List<BookConsistencyIssue> Items, int TotalCount)> GetPageWithAudiobookAsync(
        BookConsistencyIssueType? issueType, int skip, int take)
    {
        var matching = _db.BookConsistencyIssues.AsNoTracking();
        if (issueType.HasValue)
        {
            matching = matching.Where(ci => ci.IssueType == issueType.Value);
        }

        // Counted before the includes: the count does not need the audiobook or its authors, and
        // asking for them would make the query join the graph only to discard it.
        var totalCount = await matching.CountAsync();

        // The same total order as GetAllWithAudiobookAsync. It has to be total, not just by
        // AudiobookId: SQLite is free to return ties in any order, and two pages ordered only
        // partially can repeat a row and drop another.
        var items = await matching
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.IssueType)
            .ThenBy(ci => ci.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Dictionary<BookConsistencyIssueType, int>> GetCountsByTypeAsync()
    {
        return await _db.BookConsistencyIssues
            .GroupBy(ci => ci.IssueType)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<BookConsistencyIssue?> GetByIdAsync(long id)
    {
        // The full metadata graph (Authors, Narrators, Genres), not just Authors: the selective
        // tag-mismatch resolve builds a domain audiobook from this to merge the user's chosen
        // values. Reading a partially attached collection would wipe the omitted metadata on save.
        return await _db.BookConsistencyIssues
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Narrators)
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Genres)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ci => ci.Id == id);
    }

    public async Task InsertAsync(BookConsistencyIssue issue)
    {
        _db.Add(issue);
        await _db.SaveChangesAsync();
    }

    public async Task InsertRangeAsync(IEnumerable<BookConsistencyIssue> issues)
    {
        var issueList = issues as ICollection<BookConsistencyIssue> ?? issues.ToList();
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
        await _db.BookConsistencyIssues.ExecuteDeleteAsync();
        DetachTracked();
    }

    public async Task DeleteAsync(long id)
    {
        var entity = await _db.BookConsistencyIssues.FindAsync(id);
        if (entity != null)
        {
            _db.BookConsistencyIssues.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteByAudiobookIdAsync(long audiobookId)
    {
        await _db.BookConsistencyIssues
            .Where(ci => ci.AudiobookId == audiobookId)
            .ExecuteDeleteAsync();
        DetachTracked(ci => ci.AudiobookId == audiobookId);
    }

    public async Task DeleteByAudiobookIdAndTypesAsync(long audiobookId, IEnumerable<BookConsistencyIssueType> types)
    {
        var typeList = types.ToList();
        await _db.BookConsistencyIssues
            .Where(ci => ci.AudiobookId == audiobookId && typeList.Contains(ci.IssueType))
            .ExecuteDeleteAsync();
        DetachTracked(ci => ci.AudiobookId == audiobookId && typeList.Contains(ci.IssueType));
    }

    public async Task<List<BookConsistencyIssue>> GetByTypeAsync(BookConsistencyIssueType issueType)
    {
        return await _db.BookConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .Where(ci => ci.IssueType == issueType)
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Loads a whole selection in one query. The bulk resolve used to re-fetch each issue by id
    /// inside its loop - with the same Include/ThenInclude graph it had just discarded - which
    /// made resolving N issues cost N+1 queries.
    /// </summary>
    public async Task<List<BookConsistencyIssue>> GetByIdsAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return new List<BookConsistencyIssue>();
        }

        return await _db.BookConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .AsSplitQuery()
            .Where(ci => ids.Contains(ci.Id))
            .OrderBy(ci => ci.AudiobookId)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<long, int>> GetIssueSummaryAsync()
    {
        return await _db.BookConsistencyIssues
            .GroupBy(ci => ci.AudiobookId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<List<BookConsistencyIssue>> GetByAudiobookIdAsync(long audiobookId)
    {
        return await _db.BookConsistencyIssues
            .AsNoTracking()
            .Include(ci => ci.Audiobook)
                .ThenInclude(a => a.Authors)
            .Where(ci => ci.AudiobookId == audiobookId)
            .OrderBy(ci => ci.IssueType)
            .ThenBy(ci => ci.Id)
            .ToListAsync();
    }

    public async Task UpdateAsync(BookConsistencyIssue issue)
    {
        // Load-and-copy, never attach the handed-in graph: the caller typically passes an
        // entity from an AsNoTracking read that Includes the Audiobook and its Authors, and a
        // context-level Update()/Attach() would mark that entire reachable graph Modified -
        // silently overwriting concurrent edits to the book or its people with the stale
        // snapshot values, on top of the wasted per-row UPDATEs. SetValues copies scalar
        // properties only; navigations are untouched.
        var tracked = await _db.BookConsistencyIssues
            .FirstOrDefaultAsync(ci => ci.Id == issue.Id);

        if (tracked is null)
        {
            throw new KeyNotFoundException($"Consistency issue {issue.Id} does not exist.");
        }

        _db.Entry(tracked).CurrentValues.SetValues(issue);
        await _db.SaveChangesAsync();
    }

    private void DetachTracked(Func<BookConsistencyIssue, bool>? predicate = null)
    {
        foreach (var entry in _db.ChangeTracker.Entries<BookConsistencyIssue>()
                     .Where(e => predicate is null || predicate(e.Entity))
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
