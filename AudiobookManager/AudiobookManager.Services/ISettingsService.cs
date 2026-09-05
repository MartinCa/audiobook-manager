namespace AudiobookManager.Services;

public interface ISettingsService
{
    Task<Domain.SeriesMapping> CreateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<Domain.SeriesMapping> UpdateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<IList<Domain.SeriesMapping>> GetSeriesMappings();
    Task<Domain.SeriesMapping?> GetSeriesMapping(long id);
    Task DeleteSeriesMapping(long id);

    /// <summary>The UI-editable library-wide settings, bootstrapped with defaults on first read.</summary>
    Task<Domain.LibrarySettings> GetLibrarySettings();

    /// <summary>Persists new library settings and returns the saved state.</summary>
    Task<Domain.LibrarySettings> UpdateLibrarySettings(Domain.LibrarySettings settings);
}
