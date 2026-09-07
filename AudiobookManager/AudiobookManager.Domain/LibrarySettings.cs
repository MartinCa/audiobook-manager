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
}
