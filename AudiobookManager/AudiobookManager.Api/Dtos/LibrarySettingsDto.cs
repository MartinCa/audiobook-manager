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
    string UpcomingReleasesCronSchedule);

/// <summary>
/// The body of PUT api/settings/library. <see cref="InitialsPunctuation"/>,
/// <see cref="UpcomingReleasesEnabled"/> and <see cref="UpcomingReleasesCronSchedule"/> are
/// optional like <see cref="MetadataRefreshDelayMs"/>: an omitted field keeps the stored value
/// rather than resetting it.
/// </summary>
public record UpdateLibrarySettingsDto(
    string InitialsSpacing,
    string? InitialsPunctuation,
    int? MetadataRefreshDelayMs,
    bool? UpcomingReleasesEnabled,
    string? UpcomingReleasesCronSchedule);
