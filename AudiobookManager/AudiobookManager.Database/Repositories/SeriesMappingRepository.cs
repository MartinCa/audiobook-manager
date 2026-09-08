using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class SeriesMappingRepository : ISeriesMappingRepository
{
    private readonly DatabaseContext _db;

    public SeriesMappingRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<SeriesMapping> CreateSeriesMapping(SeriesMapping seriesMapping)
    {
        _db.SeriesMappings.Add(seriesMapping);
        await _db.SaveChangesAsync();

        return seriesMapping;
    }

    public async Task DeleteSeriesMapping(long id)
    {
        var entity = await _db.SeriesMappings.FindAsync(id);
        if (entity != null)
        {
            _db.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<SeriesMapping?> GetSeriesMapping(long id)
    {
        return await _db.SeriesMappings.FindAsync(id);
    }

    public async Task<(List<(string MappedSeries, List<SeriesMapping> Items)> Groups, int Total)> GetSeriesMappingGroupsAsync(
        string? search)
    {
        var query = _db.SeriesMappings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Accent-insensitive over both the pattern and the target name, per the search
            // invariant. The mappings table has no folded shadow column, so this folds per row -
            // acceptable for an operator-curated table of well under a few thousand rows.
            var pattern = $"%{AccentFolding.FoldPlain(search!.Trim())}%";
            query = query.Where(m =>
                EF.Functions.Like(AccentFolding.Fold(m.Regex), pattern) ||
                EF.Functions.Like(AccentFolding.Fold(m.MappedSeries), pattern));
        }

        // Grouped in memory rather than as an EF GroupBy: aggregating a *list* of patterns per
        // target is not expressible as a grouped query, and the input set is the curated
        // mappings table, bounded by humans maintaining it.
        var all = await query
            .OrderBy(m => m.MappedSeries)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var groups = all
            .GroupBy(m => m.MappedSeries, StringComparer.Ordinal)
            .Select(g => (g.Key, g.ToList()))
            .ToList();

        return (groups, all.Count);
    }

    public async Task<SeriesMapping> UpdateSeriesMapping(SeriesMapping seriesMapping)
    {
        _db.Update(seriesMapping);
        await _db.SaveChangesAsync();

        return seriesMapping;
    }
}
