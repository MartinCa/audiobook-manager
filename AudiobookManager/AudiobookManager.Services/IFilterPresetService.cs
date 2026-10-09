using System.Text.Json;

namespace AudiobookManager.Services;

/// <summary>A saved filter preset as the API serves it.</summary>
public record FilterPresetInfo(
    long Id,
    string Scope,
    string Name,
    IReadOnlyDictionary<string, JsonElement> Filters,
    DateTime UpdatedAt);

/// <summary>A preset name is already used by another preset of the same list (ignoring case).</summary>
public class FilterPresetNameTakenException : Exception
{
    public FilterPresetNameTakenException(string name)
        : base($"A preset named '{name}' already exists for this list.")
    {
    }
}

public interface IFilterPresetService
{
    /// <summary>
    /// The presets of one list, sorted by name. Bounded by <see cref="FilterPresetRules.MaxPresetsPerScope"/>.
    /// Throws <see cref="ArgumentException"/> for an unknown scope.
    /// </summary>
    Task<IReadOnlyList<FilterPresetInfo>> GetAsync(string scope);

    /// <summary>
    /// Saves a new preset. Throws <see cref="ArgumentException"/> (message safe to show) for an
    /// unknown scope, an invalid name, filters that do not validate for the scope or are empty, or
    /// a list already at its maximum; <see cref="FilterPresetNameTakenException"/> for a taken name.
    /// </summary>
    Task<FilterPresetInfo> CreateAsync(string scope, string name, IReadOnlyDictionary<string, JsonElement> filters);

    /// <summary>
    /// Replaces the name and filters of a preset (rename and "overwrite with the current filters"
    /// are the same call). Same validation as <see cref="CreateAsync"/>;
    /// <see cref="KeyNotFoundException"/> when there is no such preset.
    /// </summary>
    Task<FilterPresetInfo> UpdateAsync(long id, string name, IReadOnlyDictionary<string, JsonElement> filters);

    /// <summary>Deletes a preset; false when there was none.</summary>
    Task<bool> DeleteAsync(long id);
}
