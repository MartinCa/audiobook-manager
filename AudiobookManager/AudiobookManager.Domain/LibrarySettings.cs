namespace AudiobookManager.Domain;

/// <summary>
/// UI-editable library-wide settings, as served to the client over GET/PUT api/settings/library.
/// Mirrors Database.Models.LibrarySettings; the service layer maps between the two so the API
/// never sees EF entities.
/// </summary>
public class LibrarySettings
{
    public InitialsSpacing InitialsSpacing { get; set; } = InitialsSpacing.Unspaced;
}
