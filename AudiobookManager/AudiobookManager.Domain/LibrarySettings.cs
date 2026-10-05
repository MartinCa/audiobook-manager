namespace AudiobookManager.Domain;

/// <summary>
/// UI-editable library-wide settings, as served to the client over GET/PUT api/settings/library.
/// Mirrors Database.Models.LibrarySettings; the service layer maps between the two so the API
/// never sees EF entities.
/// </summary>
public class LibrarySettings
{
    public InitialsSpacing InitialsSpacing { get; set; } = InitialsSpacing.Unspaced;

    public InitialsPunctuation InitialsPunctuation { get; set; } = InitialsPunctuation.Dotted;

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

    /// <summary>
    /// Default rows-per-page for the app's paged lists. One of the fixed options the client's page
    /// size dropdown offers: 20, 50, or 100 (see <c>SettingsController.AllowedPageSizes</c>).
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// How initials in author names are written in the default online-metadata search query.
    /// See <see cref="SearchInitialsHandling"/>.
    /// </summary>
    public SearchInitialsHandling SearchInitialsHandling { get; set; } = SearchInitialsHandling.AsStored;

    /// <summary>
    /// Whether a book's folder name gets the narrator appended as <c>{Narrator}</c> (the form
    /// Audiobookshelf reads), so two narrations of one book can sit side by side. Off by default:
    /// turning it on moves every book that has a narrator, through the consistency check's
    /// WrongFilePath resolve.
    /// </summary>
    public bool IncludeNarratorInPath { get; set; }

    /// <summary>Default for <see cref="MaxNarratorsInPath"/>.</summary>
    public const int DefaultMaxNarratorsInPath = 1;

    /// <summary>Largest accepted <see cref="MaxNarratorsInPath"/>; the smallest is 1.</summary>
    public const int MaxNarratorsInPathLimit = 10;

    /// <summary>
    /// How many narrators the folder name lists when <see cref="IncludeNarratorInPath"/> is on (the
    /// first ones, in tag order). A long cast makes a long folder name and a different path for
    /// every change to the tail of the list.
    /// </summary>
    public int MaxNarratorsInPath { get; set; } = DefaultMaxNarratorsInPath;

    /// <summary>
    /// The one number the path code takes: how many narrators the folder names, 0 when the
    /// narrator is not part of the path.
    /// </summary>
    public int NarratorsInPath => IncludeNarratorInPath ? MaxNarratorsInPath : 0;

    /// <summary>
    /// Per-field rules for applying online metadata, keyed by <c>MetadataRefreshFields</c> name. A
    /// field missing from the map uses the default rule (select it, ask me), which reproduces the
    /// behaviour from before these rules existed; <c>MetadataApplyRuleSet</c> resolves the map.
    /// </summary>
    public Dictionary<string, FieldApplyRule> MetadataApplyRules { get; set; } = new();
}
