using AudiobookManager.Domain;

namespace AudiobookManager.Api.Dtos;

/// <summary>
/// The UI-editable library-wide settings. The client renders its initials-spacing control from
/// <paramref name="InitialsSpacing"/> and sends the same value back on save.
/// </summary>
public record LibrarySettingsDto(
    string InitialsSpacing,
    string InitialsPunctuation,
    int MetadataRefreshDelayMs,
    bool UpcomingReleasesEnabled,
    string UpcomingReleasesCronSchedule,
    int DefaultPageSize,
    string SearchInitialsHandling,
    bool IncludeNarratorInPath,
    int MaxNarratorsInPath,
    bool AutoSplitTitleOnColon,
    bool MainAuthorOnlyInPath,
    IReadOnlyDictionary<string, string> SearchInitialsHandlingBySource);

/// <summary>
/// The body of PUT api/settings/library. <see cref="InitialsPunctuation"/>,
/// <see cref="UpcomingReleasesEnabled"/>, <see cref="UpcomingReleasesCronSchedule"/> and
/// <see cref="DefaultPageSize"/>, <see cref="SearchInitialsHandling"/> and <see cref="IncludeNarratorInPath"/> and <see cref="MaxNarratorsInPath"/> and <see cref="AutoSplitTitleOnColon"/> and <see cref="MainAuthorOnlyInPath"/> are optional like <see cref="MetadataRefreshDelayMs"/>: an
/// omitted field keeps the stored value rather than resetting it. <see cref="SearchInitialsHandlingBySource"/>
/// (source name to handling) replaces the whole set of per-source overrides when given; a source left
/// out follows the library-wide <see cref="SearchInitialsHandling"/>.
/// </summary>
public record UpdateLibrarySettingsDto(
    string InitialsSpacing,
    string? InitialsPunctuation,
    int? MetadataRefreshDelayMs,
    bool? UpcomingReleasesEnabled,
    string? UpcomingReleasesCronSchedule,
    int? DefaultPageSize,
    string? SearchInitialsHandling = null,
    bool? IncludeNarratorInPath = null,
    int? MaxNarratorsInPath = null,
    bool? AutoSplitTitleOnColon = null,
    bool? MainAuthorOnlyInPath = null,
    Dictionary<string, string>? SearchInitialsHandlingBySource = null);

/// <summary>One selectable apply rule with the text the settings page explains it with.</summary>
public record MetadataApplyOptionDto(string Key, string Label, string Description);

/// <summary>
/// One field's apply rules plus the guard rails the page needs: whether "Always overwrite" is
/// allowed for it, and a warning to show when it is allowed but risky.
/// </summary>
public record MetadataApplyFieldDto(
    string Field,
    string Label,
    string Interactive,
    string Automated,
    bool AlwaysOverwriteAllowed,
    string? AlwaysOverwriteWarning);

/// <summary>
/// The per-field online-metadata apply rules together with the option lists, so the client holds no
/// list of fields or options of its own.
/// </summary>
public record MetadataApplyRulesDto(
    IReadOnlyList<MetadataApplyFieldDto> Fields,
    IReadOnlyList<MetadataApplyOptionDto> InteractiveOptions,
    IReadOnlyList<MetadataApplyOptionDto> AutomatedOptions);

/// <summary>One field's rules in a PUT.</summary>
public record MetadataApplyRuleInputDto(string Field, string Interactive, string Automated);

/// <summary>The body of PUT api/settings/metadata-apply-rules. A field left out keeps its stored rules.</summary>
public record UpdateMetadataApplyRulesDto(IReadOnlyList<MetadataApplyRuleInputDto> Rules);

/// <summary>
/// One "which one is the main one" choice with its two settings, the options each can take and the
/// warning to show: the client renders the tile from this and holds none of the lists.
/// </summary>
public record MetadataChoiceDto(
    string Choice,
    string Label,
    string Description,
    string WhenSourceNamesMain,
    string WhenSourceSilent,
    IReadOnlyList<MetadataApplyOptionDto> NamesMainOptions,
    IReadOnlyList<MetadataApplyOptionDto> SilentOptions,
    string? Warning);

/// <summary>The main author / primary series rules together with their option lists.</summary>
public record MetadataChoiceRulesDto(IReadOnlyList<MetadataChoiceDto> Choices);

/// <summary>One choice's rules in a PUT.</summary>
public record MetadataChoiceRuleInputDto(string Choice, string WhenSourceNamesMain, string WhenSourceSilent);

/// <summary>The body of PUT api/settings/metadata-choice-rules. A choice left out keeps its stored rules.</summary>
public record UpdateMetadataChoiceRulesDto(IReadOnlyList<MetadataChoiceRuleInputDto> Rules);

/// <summary>
/// The preferred order of the online metadata sources, as source names, first to last. Used as the
/// body of PUT api/settings/metadata-source-order and as its response (the order that was saved).
/// </summary>
public record MetadataSourceOrderDto(IReadOnlyList<string> Sources);
