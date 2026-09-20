using AudiobookManager.Database.Repositories;
using AudiobookManager.Services.MappingExtensions;

namespace AudiobookManager.Services;

public class SettingsService : ISettingsService
{
    private readonly ILibrarySettingsRepository _librarySettingsRepository;

    public SettingsService(ILibrarySettingsRepository librarySettingsRepository)
    {
        _librarySettingsRepository = librarySettingsRepository;
    }

    public async Task<Domain.LibrarySettings> GetLibrarySettings()
    {
        var dbSettings = await _librarySettingsRepository.GetOrCreateAsync();
        return dbSettings.ToDomain();
    }

    public async Task<Domain.LibrarySettings> UpdateLibrarySettings(Domain.LibrarySettings settings)
    {
        var dbSettings = await _librarySettingsRepository.UpdateAsync(
            settings.InitialsSpacing.ToDb(),
            settings.InitialsPunctuation.ToDb(),
            settings.MetadataRefreshDelayMs,
            settings.UpcomingReleasesEnabled,
            settings.UpcomingReleasesCronSchedule);
        return dbSettings.ToDomain();
    }
}
