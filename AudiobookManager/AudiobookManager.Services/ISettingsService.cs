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

    /// <summary>Saves the source order (normalized) and returns what was stored.</summary>
    /// <exception cref="ArgumentException">The list is too long or a name is too long.</exception>
    Task<IReadOnlyList<string>> UpdateMetadataSourceOrder(IReadOnlyList<string> sources);
}
