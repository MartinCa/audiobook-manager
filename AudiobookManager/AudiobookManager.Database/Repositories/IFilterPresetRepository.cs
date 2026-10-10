using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

/// <summary>What a preset write did - a taken name is an expected outcome, not an exception.</summary>
public enum FilterPresetWriteResult
{
    Saved,
    NameTaken,
    NotFound,
}

public interface IFilterPresetRepository
{
    /// <summary>
    /// The presets of one list, oldest first, capped at <paramref name="limit"/> rows. The service
    /// enforces a per-scope maximum on create, so the cap is a backstop, not a page.
    /// </summary>
    Task<List<FilterPreset>> GetByScopeAsync(string scope, int limit);

    Task<int> CountAsync(string scope);

    Task<FilterPreset?> GetByIdAsync(long id);

    /// <summary>
    /// Inserts the preset. <see cref="FilterPresetWriteResult.NameTaken"/> when another preset of
    /// the same scope already has the name (ignoring case) - including one that won a concurrent
    /// insert after the caller's own check.
    /// </summary>
    Task<(FilterPresetWriteResult Result, FilterPreset? Preset)> AddAsync(FilterPreset preset);

    /// <summary>Replaces name and filters of one preset, stamping <c>UpdatedAt</c>.</summary>
    Task<(FilterPresetWriteResult Result, FilterPreset? Preset)> UpdateAsync(
        long id, string name, string filtersJson);

    /// <summary>Deletes one preset; false when there was none.</summary>
    Task<bool> DeleteAsync(long id);
}
