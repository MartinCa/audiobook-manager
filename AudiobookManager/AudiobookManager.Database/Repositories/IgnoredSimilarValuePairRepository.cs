using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class IgnoredSimilarValuePairRepository : IIgnoredSimilarValuePairRepository
{
    private readonly DatabaseContext _db;

    public IgnoredSimilarValuePairRepository(DatabaseContext db)
    {
        _db = db;
    }

    public Task<List<IgnoredSimilarValuePair>> GetForKindAsync(string kind) =>
        _db.IgnoredSimilarValuePairs
            .AsNoTracking()
            .Where(p => p.Kind == kind)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();

    public async Task AddRangeAsync(string kind, IEnumerable<(string ValueA, string ValueB)> pairs)
    {
        var distinctPairs = pairs.Distinct().ToList();
        if (distinctPairs.Count == 0)
        {
            return;
        }

        var existing = await _db.IgnoredSimilarValuePairs
            .Where(p => p.Kind == kind)
            .Select(p => new { p.ValueA, p.ValueB })
            .ToListAsync();
        var existingSet = existing.Select(p => (p.ValueA, p.ValueB)).ToHashSet();

        var missing = distinctPairs.Where(p => !existingSet.Contains(p)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _db.IgnoredSimilarValuePairs.AddRange(missing.Select(p => new IgnoredSimilarValuePair
        {
            Kind = kind,
            ValueA = p.ValueA,
            ValueB = p.ValueB,
            CreatedAt = now,
        }));

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Lost a race against a concurrent ignore of the same pair (or same kind/value pair
            // added from another request) - the row already exists, which is exactly what this
            // call asked for. Detach so the failed insert does not linger in the change tracker.
            foreach (var entry in _db.ChangeTracker.Entries<IgnoredSimilarValuePair>()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    public async Task DeleteAsync(string kind, long id)
    {
        await _db.IgnoredSimilarValuePairs
            .Where(p => p.Id == id && p.Kind == kind)
            .ExecuteDeleteAsync();
    }
}
