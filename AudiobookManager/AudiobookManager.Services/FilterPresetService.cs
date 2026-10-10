using System.Text.Json;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;

namespace AudiobookManager.Services;

public class FilterPresetService : IFilterPresetService
{
    private readonly IFilterPresetRepository _repository;

    public FilterPresetService(IFilterPresetRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<FilterPresetInfo>> GetAsync(string scope)
    {
        var normalizedScope = FilterPresetRules.NormalizeScope(scope);
        var presets = await _repository.GetByScopeAsync(normalizedScope, FilterPresetRules.MaxPresetsPerScope);

        // Presented in name order, sorted here: SQLite would order by code point, putting "Zebra"
        // ahead of "apple". Id breaks ties so the order is total.
        return presets
            .OrderBy(p => p.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(p => p.Id)
            .Select(ToInfo)
            .ToList();
    }

    public async Task<FilterPresetInfo> CreateAsync(
        string scope, string name, IReadOnlyDictionary<string, JsonElement> filters)
    {
        var normalizedScope = FilterPresetRules.NormalizeScope(scope);
        var normalizedName = FilterPresetRules.NormalizeName(name);
        var filtersJson = FilterPresetRules.NormalizeFilters(normalizedScope, filters);

        // A soft cap: two concurrent creates can both pass it and land one over, which only
        // matters as a bound on the list's size, not as a correctness rule.
        if (await _repository.CountAsync(normalizedScope) >= FilterPresetRules.MaxPresetsPerScope)
        {
            throw new ArgumentException(
                $"A list can hold at most {FilterPresetRules.MaxPresetsPerScope} presets. Delete one to save another.");
        }

        var (result, saved) = await _repository.AddAsync(new FilterPreset
        {
            Scope = normalizedScope,
            Name = normalizedName,
            FiltersJson = filtersJson,
        });

        return result == FilterPresetWriteResult.Saved && saved is not null
            ? ToInfo(saved)
            : throw new FilterPresetNameTakenException(normalizedName);
    }

    public async Task<FilterPresetInfo> UpdateAsync(
        long id, string name, IReadOnlyDictionary<string, JsonElement> filters)
    {
        var existing = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Filter preset {id} does not exist.");

        var normalizedName = FilterPresetRules.NormalizeName(name);
        var filtersJson = FilterPresetRules.NormalizeFilters(existing.Scope, filters);

        var (result, saved) = await _repository.UpdateAsync(id, normalizedName, filtersJson);
        return result switch
        {
            FilterPresetWriteResult.Saved when saved is not null => ToInfo(saved),
            FilterPresetWriteResult.NameTaken => throw new FilterPresetNameTakenException(normalizedName),
            _ => throw new KeyNotFoundException($"Filter preset {id} does not exist."),
        };
    }

    public Task<bool> DeleteAsync(long id) => _repository.DeleteAsync(id);

    private static FilterPresetInfo ToInfo(FilterPreset preset) => new(
        preset.Id,
        preset.Scope,
        preset.Name,
        FilterPresetRules.ParseStored(preset.FiltersJson),
        DateTime.SpecifyKind(preset.UpdatedAt, DateTimeKind.Utc));
}
