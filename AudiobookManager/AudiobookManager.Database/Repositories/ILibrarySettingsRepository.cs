using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface ILibrarySettingsRepository
{
    /// <summary>The single settings row, creating it with defaults on first access.</summary>
    Task<LibrarySettings> GetOrCreateAsync();

    /// <summary>Updates the settings row (creating it if missing) and returns the saved row.</summary>
    Task<LibrarySettings> UpdateAsync(
        InitialsSpacing initialsSpacing,
        InitialsPunctuation initialsPunctuation,
        int metadataRefreshDelayMs,
        bool upcomingReleasesEnabled,
        string upcomingReleasesCronSchedule,
        int defaultPageSize,
        SearchInitialsHandling searchInitialsHandling);

    /// <summary>
    /// Replaces only the online-metadata apply rules JSON (null = all defaults), leaving every other
    /// setting untouched, so saving the rules can never clobber a concurrent edit of the rest.
    /// </summary>
    Task SetMetadataApplyRulesJsonAsync(string? json);
}
