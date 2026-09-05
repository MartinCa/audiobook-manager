using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface ILibrarySettingsRepository
{
    /// <summary>The single settings row, creating it with defaults on first access.</summary>
    Task<LibrarySettings> GetOrCreateAsync();

    /// <summary>Updates the settings row (creating it if missing) and returns the saved row.</summary>
    Task<LibrarySettings> UpdateAsync(InitialsSpacing initialsSpacing);
}
