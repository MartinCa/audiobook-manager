using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class QualifierIndicatorRepository : IQualifierIndicatorRepository
{
    /// <summary>
    /// The bound for the "no unbounded lists over the wire" invariant. The rules are a small,
    /// human-maintained configuration list (a handful of wordings per source), so this is a
    /// truss against a pathological row count, not a sizing affordance. The write path refuses
    /// a larger set, and the read is capped with <c>Take</c> inside the query.
    /// </summary>
    public const int MaxRules = 500;

    private readonly DatabaseContext _db;

    public QualifierIndicatorRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<List<QualifierIndicator>> GetAllAsync() =>
        await _db.QualifierIndicators
            .AsNoTracking()
            .OrderBy(r => r.Source)
            .ThenBy(r => r.Indicator)
            .ThenBy(r => r.Id)
            .Take(MaxRules)
            .ToListAsync();

    public async Task<List<QualifierIndicator>> ReplaceAllAsync(IReadOnlyCollection<QualifierIndicator> rules)
    {
        if (rules.Count > MaxRules)
        {
            throw new ArgumentException($"At most {MaxRules} qualifier indicators can be configured.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _db.QualifierIndicators.ExecuteDeleteAsync();

        var fresh = rules
            .Select(r => new QualifierIndicator(0, r.Source, r.Indicator, r.QualifierKey))
            .ToList();
        _db.QualifierIndicators.AddRange(fresh);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // The caller de-duplicates, so this is only a concurrent writer: a 4xx, not a 500.
            throw new ArgumentException("Two qualifier indicators have the same source and indicator.");
        }
        finally
        {
            // Bulk delete bypassed the tracker and SQLite reuses rowids, so nothing added here may
            // stay in the identity map for a later read in this scope to resolve to.
            foreach (var entry in _db.ChangeTracker.Entries<QualifierIndicator>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        await transaction.CommitAsync();
        return await GetAllAsync();
    }
}
