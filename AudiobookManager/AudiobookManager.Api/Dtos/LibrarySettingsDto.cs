using AudiobookManager.Domain;

namespace AudiobookManager.Api.Dtos;

/// <summary>
/// The UI-editable library-wide settings. The client renders its initials-spacing control from
/// <paramref name="InitialsSpacing"/> and sends the same value back on save.
/// </summary>
public record LibrarySettingsDto(string InitialsSpacing);

/// <summary>The body of PUT api/settings/library.</summary>
public record UpdateLibrarySettingsDto(string InitialsSpacing);
