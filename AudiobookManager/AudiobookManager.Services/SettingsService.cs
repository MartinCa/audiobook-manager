using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services.MappingExtensions;

namespace AudiobookManager.Services;

public class SettingsService : ISettingsService
{
    private readonly ILibrarySettingsRepository _librarySettingsRepository;
    private readonly IEnumerable<IScraper> _scrapers;

    public SettingsService(ILibrarySettingsRepository librarySettingsRepository, IEnumerable<IScraper> scrapers)
    {
        _librarySettingsRepository = librarySettingsRepository;
        _scrapers = scrapers;
    }

    public async Task<Domain.LibrarySettings> GetLibrarySettings()
    {
        var dbSettings = await _librarySettingsRepository.GetOrCreateAsync();
        return dbSettings.ToDomain();
    }

    public async Task<Domain.LibrarySettings> UpdateLibrarySettings(Domain.LibrarySettings settings)
    {
        // The per-source overrides are validated and written first: a bad source name must fail the
        // whole save before any other setting changes, and the row UpdateAsync returns must already
        // carry them (the JSON is written set-based, bypassing the tracker).
        var registered = _scrapers.Select(s => s.SourceName).ToList();
        var bySource = MetadataSourceSearchSettings.Normalize(settings.SearchInitialsHandlingBySource)
            .ToDictionary(
                e => registered.FirstOrDefault(r => string.Equals(r, e.Key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"'{e.Key}' is not a known metadata source."),
                e => e.Value,
                StringComparer.OrdinalIgnoreCase);
        MetadataSourceSearchSettings.Validate(bySource);
        await _librarySettingsRepository.SetMetadataSourceSearchJsonAsync(MetadataSourceSearchSettings.Serialize(bySource));

        var dbSettings = await _librarySettingsRepository.UpdateAsync(
            settings.InitialsSpacing.ToDb(),
            settings.InitialsPunctuation.ToDb(),
            settings.MetadataRefreshDelayMs,
            settings.UpcomingReleasesEnabled,
            settings.UpcomingReleasesCronSchedule,
            settings.DefaultPageSize,
            settings.SearchInitialsHandling.ToDb(),
            settings.IncludeNarratorInPath,
            settings.MaxNarratorsInPath,
            settings.AutoSplitTitleOnColon,
            settings.MainAuthorOnlyInPath);
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

    public async Task<IReadOnlyList<string>> GetMetadataSourceOrder() =>
        MetadataSourceOrder.Deserialize((await _librarySettingsRepository.GetOrCreateAsync()).MetadataSourceOrderJson);

    public async Task<IReadOnlyList<string>> UpdateMetadataSourceOrder(IReadOnlyList<string> sources)
    {
        var registered = _scrapers.Select(s => s.SourceName).ToList();
        var normalized = MetadataSourceOrder.Normalize(sources)
            .Select(name => registered.FirstOrDefault(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"'{name}' is not a known metadata source."))
            .ToList();
        MetadataSourceOrder.Validate(normalized);
        await _librarySettingsRepository.SetMetadataSourceOrderJsonAsync(MetadataSourceOrder.Serialize(normalized));
        return normalized;
    }
}
