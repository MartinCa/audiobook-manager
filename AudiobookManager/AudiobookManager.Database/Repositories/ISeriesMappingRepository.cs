using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface ISeriesMappingRepository
{
    /// <summary>
    /// The mapping patterns owned by one series, in insertion order. The list is per-series and
    /// operator-curated (each row is a human-maintained regex; the global regex uniqueness means one
    /// row per pattern across the whole table), so unlike the growing data tables it is returned
    /// whole rather than paged, exactly like the grouped Settings list it replaces.
    /// </summary>
    Task<List<SeriesMapping>> GetBySeriesNameAsync(string seriesName);

    Task<SeriesMapping> CreateSeriesMappingAsync(SeriesMapping seriesMapping);

    /// <summary>Read-only lookup of one mapping row (with its owner id), or null when the id is unknown.</summary>
    Task<SeriesMapping?> GetSeriesMappingAsync(long id);

    /// <summary>Updates Regex/WarnAboutPart on an existing row; null when the id is unknown.</summary>
    Task<SeriesMapping?> UpdateSeriesMappingAsync(SeriesMapping seriesMapping);

    /// <summary>Deletes one mapping row; false when no row had the id.</summary>
    Task<bool> DeleteSeriesMappingAsync(long id);
}
