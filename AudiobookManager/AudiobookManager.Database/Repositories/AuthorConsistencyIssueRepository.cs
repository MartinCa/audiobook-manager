using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class AuthorConsistencyIssueRepository : IAuthorConsistencyIssueRepository
{
    private readonly DatabaseContext _db;

    public AuthorConsistencyIssueRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<(List<AuthorConsistencyIssue> Items, int TotalCount)> GetPageWithAuthorAsync(int skip, int take)
    {
        var matching = _db.AuthorConsistencyIssues.AsNoTracking();

        var totalCount = await matching.CountAsync();

        var items = await matching
            .Include(i => i.Person)
            .OrderByDescending(i => i.DetectedAt)
            .ThenBy(i => i.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task UpsertFailureAsync(long personId, string errorMessage)
    {
        var existing = await _db.AuthorConsistencyIssues
            .FirstOrDefaultAsync(i => i.PersonId == personId);

        if (existing is null)
        {
            _db.Add(new AuthorConsistencyIssue
            {
                PersonId = personId,
                ErrorMessage = errorMessage,
                DetectedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.ErrorMessage = errorMessage;
            existing.DetectedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteByPersonIdAsync(long personId)
    {
        await _db.AuthorConsistencyIssues
            .Where(i => i.PersonId == personId)
            .ExecuteDeleteAsync();

        foreach (var entry in _db.ChangeTracker.Entries<AuthorConsistencyIssue>()
                     .Where(e => e.Entity.PersonId == personId)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
