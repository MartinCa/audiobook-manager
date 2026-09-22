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
            // CreatedAt alone is not a total order: every pair from one AddRangeAsync batch shares
            // the same timestamp, so same-batch rows would otherwise come back in an arbitrary,
            // rowid-dependent order between requests.
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
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
            // call asked for. But SaveChangesAsync batches the whole AddRange into one statement,
            // and a single unique violation aborts that entire batch - so a caller ignoring a
            // value against several others in its group (IgnorePairAsync fans out one pair per
            // "against" value) would silently lose every pair after the one that collided, not
            // just the collided one. Detach the whole batch and retry each pair on its own
            // SaveChanges so only the pair(s) that actually lost the race are skipped.
            DetachAddedEntries();

            foreach (var pair in missing)
            {
                _db.IgnoredSimilarValuePairs.Add(new IgnoredSimilarValuePair
                {
                    Kind = kind,
                    ValueA = pair.ValueA,
                    ValueB = pair.ValueB,
                    CreatedAt = now,
                });

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException retryEx) when (SqliteErrors.IsUniqueViolation(retryEx))
                {
                    DetachAddedEntries();
                }
            }
        }
    }

    private void DetachAddedEntries()
    {
        foreach (var entry in _db.ChangeTracker.Entries<IgnoredSimilarValuePair>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task DeleteAsync(string kind, long id)
    {
        await _db.IgnoredSimilarValuePairs
            .Where(p => p.Id == id && p.Kind == kind)
            .ExecuteDeleteAsync();
    }

    public async Task DeleteInvolvingValuesAsync(string kind, IReadOnlyCollection<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        await _db.IgnoredSimilarValuePairs
            .Where(p => p.Kind == kind && (values.Contains(p.ValueA) || values.Contains(p.ValueB)))
            .ExecuteDeleteAsync();
    }
}
