namespace AudiobookManager.Services;

public interface ISettingsService
{
    /// <summary>The UI-editable library-wide settings, bootstrapped with defaults on first read.</summary>
    Task<Domain.LibrarySettings> GetLibrarySettings();

    /// <summary>Persists new library settings and returns the saved state.</summary>
    Task<Domain.LibrarySettings> UpdateLibrarySettings(Domain.LibrarySettings settings);

    /// <summary>The online-metadata apply rules, resolved against the defaults.</summary>
    Task<MetadataApplyRuleSet> GetMetadataApplyRules();

    /// <summary>
    /// Saves the rules for the fields named in <paramref name="rules"/> (the rest keep their stored
    /// rule) and returns the resulting set.
    /// </summary>
    /// <exception cref="ArgumentException">A field is unknown or a rule is not allowed for it.</exception>
    Task<MetadataApplyRuleSet> UpdateMetadataApplyRules(IReadOnlyDictionary<string, Domain.FieldApplyRule> rules);

    /// <summary>
    /// The saved order of the online metadata sources, by name (empty when none was chosen). Apply
    /// it with <see cref="Domain.MetadataSourceOrder.Sort{T}"/> wherever sources are listed.
    /// </summary>
    Task<IReadOnlyList<string>> GetMetadataSourceOrder();

    /// <summary>
    /// Saves the source order and returns what was stored: names are matched to the registered
    /// sources ignoring case and stored in their registered spelling.
    /// </summary>
    /// <exception cref="ArgumentException">A name is not a registered source, or the list is too long.</exception>
    Task<IReadOnlyList<string>> UpdateMetadataSourceOrder(IReadOnlyList<string> sources);
}
