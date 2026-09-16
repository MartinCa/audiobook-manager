using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class SeriesMappingRepository : ISeriesMappingRepository
{
    /// <summary>
    /// The per-series mapping list cap - the bound for the "no unbounded lists over the wire"
    /// invariant, enforced at the query boundary with <c>Take</c> so the database never even
    /// materializes more rows than this. A series' patterns are human-maintained regex rows
    /// added one dialog at a time (the review of the series-scoped mappings endpoint flagged its
    /// unbounded fetch), so a real series stays far under this; it is a defensive truss against
    /// a pathological row count, not a sizing affordance.
    /// </summary>
    public const int MaxMappingsPerSeries = 250;

    private readonly DatabaseContext _db;

    public SeriesMappingRepository(DatabaseContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The mapping patterns owned by one series, in insertion order, capped at
    /// <see cref="MaxMappingsPerSeries"/> inside the query.
    /// </summary>
    public async Task<List<SeriesMapping>> GetBySeriesNameAsync(string seriesName)
    {
        return await _db.SeriesMappings
            .AsNoTracking()
            .Where(m => m.Series!.Name == seriesName)
            .OrderBy(m => m.Id)
            .Take(MaxMappingsPerSeries)
            .ToListAsync();
    }

    public async Task<SeriesMapping> CreateSeriesMappingAsync(SeriesMapping seriesMapping)
    {
        _db.SeriesMappings.Add(seriesMapping);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // regex is globally unique (first-match determinism), so a duplicate pattern can
            // only come from a user error the caller should hear about as a 4xx, never a 500.
            _db.Entry(seriesMapping).State = EntityState.Detached;
            throw new ArgumentException($"A series mapping with the pattern '{seriesMapping.Regex}' already exists.");
        }

        return seriesMapping;
    }

    /// <summary>
    /// Lookup of one mapping row (with its owner id), or null when the id is unknown. Deliberately
    /// TRACKED, not AsNoTracking: the only callers are the update/delete ownership checks in
    /// <c>SeriesService</c>, which hand the id straight to
    /// <see cref="UpdateSeriesMappingAsync"/> / <see cref="DeleteSeriesMappingAsync"/> in the same
    /// request scope, and those re-fetch the row via <c>FindAsync</c>. A tracked fetch puts the
    /// row in the identity map, so the second lookup short-circuits there instead of issuing a
    /// second SELECT. (An AsNoTracking fetch forked the row instead, so every edit/delete cost two
    /// reads of the same row.)
    /// </summary>
    public async Task<SeriesMapping?> GetSeriesMappingAsync(long id)
    {
        return await _db.SeriesMappings
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<SeriesMapping?> UpdateSeriesMappingAsync(SeriesMapping seriesMapping)
    {
        var existing = await _db.SeriesMappings.FindAsync(seriesMapping.Id);
        if (existing is null)
        {
            return null;
        }

        existing.Regex = seriesMapping.Regex;
        existing.WarnAboutPart = seriesMapping.WarnAboutPart;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Same duplicate-pattern path as CreateSeriesMappingAsync: a 4xx, not a 500.
            throw new ArgumentException($"A series mapping with the pattern '{seriesMapping.Regex}' already exists.");
        }

        return existing;
    }

    public async Task<bool> DeleteSeriesMappingAsync(long id)
    {
        var entity = await _db.SeriesMappings.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }
}
