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
    int MaxNarratorsInPath);

/// <summary>
/// The body of PUT api/settings/library. <see cref="InitialsPunctuation"/>,
/// <see cref="UpcomingReleasesEnabled"/>, <see cref="UpcomingReleasesCronSchedule"/> and
/// <see cref="DefaultPageSize"/>, <see cref="SearchInitialsHandling"/> and <see cref="IncludeNarratorInPath"/> and <see cref="MaxNarratorsInPath"/> are optional like <see cref="MetadataRefreshDelayMs"/>: an
/// omitted field keeps the stored value rather than resetting it.
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
    int? MaxNarratorsInPath = null);

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
