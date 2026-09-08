namespace AudiobookManager.Services;

public interface ISettingsService
{
    Task<Domain.SeriesMapping> CreateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<Domain.SeriesMapping> UpdateSeriesMapping(Domain.SeriesMapping seriesMapping);

    /// <summary>
    /// The series mappings grouped by target series name, for the Settings page: the flat
    /// unbounded list it replaces was removed. <paramref name="search"/> is the
    /// accent-insensitive filter over pattern and target name, applied server-side.
    /// </summary>
    Task<Domain.SeriesMappingGroups> GetSeriesMappingGroupsAsync(string? search);
    Task<Domain.SeriesMapping?> GetSeriesMapping(long id);
    Task DeleteSeriesMapping(long id);

    /// <summary>The UI-editable library-wide settings, bootstrapped with defaults on first read.</summary>
    Task<Domain.LibrarySettings> GetLibrarySettings();

    /// <summary>Persists new library settings and returns the saved state.</summary>
    Task<Domain.LibrarySettings> UpdateLibrarySettings(Domain.LibrarySettings settings);
}
