using AudiobookManager.Database.Repositories;
using AudiobookManager.Services.MappingExtensions;

namespace AudiobookManager.Services;

public class SettingsService : ISettingsService
{
    private readonly ILibrarySettingsRepository _librarySettingsRepository;

    public SettingsService(ILibrarySettingsRepository librarySettingsRepository)
    {
        _librarySettingsRepository = librarySettingsRepository;
    }

    public async Task<Domain.LibrarySettings> GetLibrarySettings()
    {
        var dbSettings = await _librarySettingsRepository.GetOrCreateAsync();
        return dbSettings.ToDomain();
    }

    public async Task<Domain.LibrarySettings> UpdateLibrarySettings(Domain.LibrarySettings settings)
    {
        var dbSettings = await _librarySettingsRepository.UpdateAsync(
            settings.InitialsSpacing.ToDb(),
            settings.InitialsPunctuation.ToDb(),
            settings.MetadataRefreshDelayMs,
            settings.UpcomingReleasesEnabled,
            settings.UpcomingReleasesCronSchedule,
            settings.DefaultPageSize,
            settings.SearchInitialsHandling.ToDb(),
            settings.IncludeNarratorInPath,
            settings.MaxNarratorsInPath);
        return dbSettings.ToDomain();
    }

    public async Task<MetadataApplyRuleSet> GetMetadataApplyRules()
    {
        var dbSettings = await _librarySettingsRepository.GetOrCreateAsync();
        return MetadataApplyRuleSet.From(MetadataApplyRuleSet.Deserialize(dbSettings.MetadataApplyRulesJson));
    }

    public async Task<MetadataApplyRuleSet> UpdateMetadataApplyRules(IReadOnlyDictionary<string, Domain.FieldApplyRule> rules)
    {
        MetadataApplyRuleSet.Validate(rules);

        var merged = MetadataApplyRuleSet.From(MetadataApplyRuleSet.Deserialize(
            (await _librarySettingsRepository.GetOrCreateAsync()).MetadataApplyRulesJson)).ToDictionary()
            .ToDictionary(r => r.Key, r => r.Value);
        foreach (var (field, rule) in rules)
        {
            merged[field] = rule;
        }

        var resolved = MetadataApplyRuleSet.From(merged);
        await _librarySettingsRepository.SetMetadataApplyRulesJsonAsync(resolved.Serialize());
        return resolved;
    }
}
