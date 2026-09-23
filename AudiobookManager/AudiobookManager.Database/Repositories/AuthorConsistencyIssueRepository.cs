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

        if (existing is not null)
        {
            Apply(existing, errorMessage);
            await _db.SaveChangesAsync();
            return;
        }

        var issue = new AuthorConsistencyIssue
        {
            PersonId = personId,
            ErrorMessage = errorMessage,
            DetectedAt = DateTime.UtcNow,
        };
        _db.Add(issue);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // person_id is unique and this reads before it inserts, across an await on a
            // request-scoped context - RefreshAuthorRosterTrackedAsync is reachable concurrently
            // from three controller endpoints, so two failing refreshes for the same author can
            // both miss this read and both insert. Adopt the winner's row the same way
            // PendingSeriesRefreshRepository.UpsertAsync does.
            _db.Entry(issue).State = EntityState.Detached;

            var winner = await _db.AuthorConsistencyIssues
                .FirstOrDefaultAsync(i => i.PersonId == personId);
            if (winner is null)
            {
                // Some other uniqueness constraint failed - not the race this handler is for.
                throw;
            }

            Apply(winner, errorMessage);
            await _db.SaveChangesAsync();
        }
    }

    private static void Apply(AuthorConsistencyIssue target, string errorMessage)
    {
        target.ErrorMessage = errorMessage;
        target.DetectedAt = DateTime.UtcNow;
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
