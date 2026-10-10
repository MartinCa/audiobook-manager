using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class FilterPresetRepository : IFilterPresetRepository
{
    private readonly DatabaseContext _db;

    public FilterPresetRepository(DatabaseContext db)
    {
        _db = db;
    }

    public Task<List<FilterPreset>> GetByScopeAsync(string scope, int limit) =>
        _db.FilterPresets
            .AsNoTracking()
            .Where(p => p.Scope == scope)
            // Id is the total order (a creation order); the service sorts the bounded result for
            // presentation, since SQLite's collation would put "Zebra" before "apple".
            .OrderBy(p => p.Id)
            .Take(limit)
            .ToListAsync();

    public Task<int> CountAsync(string scope) =>
        _db.FilterPresets.AsNoTracking().CountAsync(p => p.Scope == scope);

    public Task<FilterPreset?> GetByIdAsync(long id) =>
        _db.FilterPresets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(FilterPresetWriteResult Result, FilterPreset? Preset)> AddAsync(FilterPreset preset)
    {
        var now = DateTime.UtcNow;
        preset.CreatedAt = now;
        preset.UpdatedAt = now;
        _db.FilterPresets.Add(preset);

        try
        {
            await _db.SaveChangesAsync();
            return (FilterPresetWriteResult.Saved, preset);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Two requests can both pass the service's name check; the unique index decides.
            _db.Entry(preset).State = EntityState.Detached;
            return (FilterPresetWriteResult.NameTaken, null);
        }
    }

    public async Task<(FilterPresetWriteResult Result, FilterPreset? Preset)> UpdateAsync(
        long id, string name, string filtersJson)
    {
        var preset = await _db.FilterPresets.FirstOrDefaultAsync(p => p.Id == id);
        if (preset is null)
        {
            return (FilterPresetWriteResult.NotFound, null);
        }

        preset.Name = name;
        preset.FiltersJson = filtersJson;
        preset.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
            return (FilterPresetWriteResult.Saved, preset);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            _db.Entry(preset).State = EntityState.Detached;
            return (FilterPresetWriteResult.NameTaken, null);
        }
    }

    public async Task<bool> DeleteAsync(long id) =>
        await _db.FilterPresets.Where(p => p.Id == id).ExecuteDeleteAsync() > 0;
}
