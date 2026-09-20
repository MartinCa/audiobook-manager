namespace AudiobookManager.Domain;

/// <summary>
/// UI-editable library-wide settings, as served to the client over GET/PUT api/settings/library.
/// Mirrors Database.Models.LibrarySettings; the service layer maps between the two so the API
/// never sees EF entities.
/// </summary>
public class LibrarySettings
{
    public InitialsSpacing InitialsSpacing { get; set; } = InitialsSpacing.Unspaced;

    /// <summary>
    /// Delay the bulk metadata-refresh loop waits between consecutive source requests
    /// (milliseconds). Applies to the bulk loop only; a single-book refresh never waits.
    /// </summary>
    public int MetadataRefreshDelayMs { get; set; } = 1000;

    /// <summary>Whether the upcoming-releases worker's scheduled sweep runs at all.</summary>
    public bool UpcomingReleasesEnabled { get; set; } = true;

    /// <summary>
    /// Standard 5-field cron expression (minute hour day month weekday), evaluated in UTC, that
    /// schedules the upcoming-releases worker's sweep. Defaults to once a day at 03:00 UTC -
    /// release dates don't change minute to minute, and this keeps the feature well within the
    /// Hardcover daily request budget alongside everything else that shares it (series refresh,
    /// metadata refresh, search).
    /// </summary>
    public string UpcomingReleasesCronSchedule { get; set; } = "0 3 * * *";
}
