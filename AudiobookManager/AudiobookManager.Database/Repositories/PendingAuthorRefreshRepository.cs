using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class PendingAuthorRefreshRepository : IPendingAuthorRefreshRepository
{
    private readonly DatabaseContext _db;

    public PendingAuthorRefreshRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task UpsertAsync(long personId, string proposedName, string sourceName, string? sourceUrl)
    {
        var existing = await _db.PendingAuthorRefreshes.FirstOrDefaultAsync(p => p.PersonId == personId);
        if (existing is not null)
        {
            Apply(existing, proposedName, sourceName, sourceUrl);
            await _db.SaveChangesAsync();
            return;
        }

        var pending = new PendingAuthorRefresh { PersonId = personId };
        Apply(pending, proposedName, sourceName, sourceUrl);
        _db.Add(pending);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // person_id is unique and this reads before it inserts across an await: adopt the
            // winner's row rather than failing the refresh that found the difference.
            _db.Entry(pending).State = EntityState.Detached;

            var winner = await _db.PendingAuthorRefreshes.FirstOrDefaultAsync(p => p.PersonId == personId);
            if (winner is null)
            {
                throw;
            }

            Apply(winner, proposedName, sourceName, sourceUrl);
            await _db.SaveChangesAsync();
        }
    }

    private static void Apply(PendingAuthorRefresh target, string proposedName, string sourceName, string? sourceUrl)
    {
        target.ProposedName = proposedName;
        target.SourceName = sourceName;
        target.SourceUrl = sourceUrl;
        target.FetchedAt = DateTime.UtcNow;
    }

    public async Task DeleteByPersonIdAsync(long personId)
    {
        await _db.PendingAuthorRefreshes
            .Where(p => p.PersonId == personId)
            .ExecuteDeleteAsync();

        // Set-based delete bypasses the change tracker: drop any tracked copy so a later insert
        // that reuses the rowid is not resolved back to the deleted entity.
        foreach (var entry in _db.ChangeTracker.Entries<PendingAuthorRefresh>()
                     .Where(e => e.Entity.PersonId == personId)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task<PendingAuthorRefresh?> GetByPersonIdAsync(long personId) =>
        _db.PendingAuthorRefreshes
            .AsNoTracking()
            .Include(p => p.Person)
            .FirstOrDefaultAsync(p => p.PersonId == personId);

    public async Task<(List<PendingAuthorRefresh> Items, int TotalCount)> GetPageWithAuthorAsync(int skip, int take)
    {
        var matching = _db.PendingAuthorRefreshes.AsNoTracking();

        var totalCount = await matching.CountAsync();

        var items = await matching
            .Include(p => p.Person)
            .OrderByDescending(p => p.FetchedAt)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, totalCount);
    }

    public Task<int> CountAsync() => _db.PendingAuthorRefreshes.CountAsync();
}
