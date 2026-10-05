using AudiobookManager.Database.Models;

using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class LibrarySettingsRepository : ILibrarySettingsRepository
{
    private readonly DatabaseContext _db;

    public LibrarySettingsRepository(DatabaseContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The settings row, creating it on first access so callers never handle a null. The row's
    /// id is the fixed SingletonId, so the primary key is the guard: two scopes racing the
    /// bootstrap both insert id 1 and only one commit wins; the loser re-reads the winner's row
    /// (the same adopt-the-winner pattern PersonRepository.GetOrCreatePersons uses against its
    /// unique index).
    /// </summary>
    public async Task<LibrarySettings> GetOrCreateAsync()
    {
        var settings = await _db.LibrarySettings.AsNoTracking().SingleOrDefaultAsync();
        if (settings != null)
        {
            return settings;
        }

        var created = new LibrarySettings(LibrarySettings.SingletonId, InitialsSpacing.Unspaced);
        _db.LibrarySettings.Add(created);

        try
        {
            await _db.SaveChangesAsync();
            return created;
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            _db.Entry(created).State = EntityState.Detached;
            return await _db.LibrarySettings.AsNoTracking().SingleAsync();
        }
    }

    public async Task<LibrarySettings> UpdateAsync(
        InitialsSpacing initialsSpacing,
        InitialsPunctuation initialsPunctuation,
        int metadataRefreshDelayMs,
        bool upcomingReleasesEnabled,
        string upcomingReleasesCronSchedule,
        int defaultPageSize,
        SearchInitialsHandling searchInitialsHandling,
        bool includeNarratorInPath,
        int maxNarratorsInPath)
    {
        var settings = await _db.LibrarySettings.SingleOrDefaultAsync();
        if (settings == null)
        {
            settings = new LibrarySettings(
                LibrarySettings.SingletonId,
                initialsSpacing,
                initialsPunctuation,
                metadataRefreshDelayMs,
                upcomingReleasesEnabled,
                upcomingReleasesCronSchedule,
                defaultPageSize,
                searchInitialsHandling,
                includeNarratorInPath,
                maxNarratorsInPath);
            _db.LibrarySettings.Add(settings);
        }
        else
        {
            settings.InitialsSpacing = initialsSpacing;
            settings.InitialsPunctuation = initialsPunctuation;
            settings.MetadataRefreshDelayMs = metadataRefreshDelayMs;
            settings.UpcomingReleasesEnabled = upcomingReleasesEnabled;
            settings.UpcomingReleasesCronSchedule = upcomingReleasesCronSchedule;
            settings.DefaultPageSize = defaultPageSize;
            settings.SearchInitialsHandling = searchInitialsHandling;
            settings.IncludeNarratorInPath = includeNarratorInPath;
            settings.MaxNarratorsInPath = maxNarratorsInPath;
        }

        try
        {
            await _db.SaveChangesAsync();
            return settings;
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // The insert raced for the singleton id (1): a second bootstrap won and this instance
            // must apply its update to the winner's row instead of surfacing a UNIQUE violation.
            _db.Entry(settings).State = EntityState.Detached;

            var winner = await _db.LibrarySettings.SingleAsync();
            winner.InitialsSpacing = initialsSpacing;
            winner.InitialsPunctuation = initialsPunctuation;
            winner.MetadataRefreshDelayMs = metadataRefreshDelayMs;
            winner.UpcomingReleasesEnabled = upcomingReleasesEnabled;
            winner.UpcomingReleasesCronSchedule = upcomingReleasesCronSchedule;
            winner.DefaultPageSize = defaultPageSize;
            winner.SearchInitialsHandling = searchInitialsHandling;
            winner.IncludeNarratorInPath = includeNarratorInPath;
            winner.MaxNarratorsInPath = maxNarratorsInPath;
            await _db.SaveChangesAsync();
            return winner;
        }
    }

    public async Task SetMetadataApplyRulesJsonAsync(string? json)
    {
        // Ensure the singleton row exists, then update just this column set-based.
        await GetOrCreateAsync();
        await _db.LibrarySettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.MetadataApplyRulesJson, json));
    }
}
