using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface ISeriesMappingRepository
{
    /// <summary>
    /// The mapping patterns owned by one series, in insertion order. This is the bounded-list
    /// invariant's explicit limit: the query itself is capped at
    /// <see cref="SeriesMappingRepository.MaxMappingsPerSeries"/> rows (a curator-sized bound -
    /// patterns are human-maintained regex rows added one dialog at a time), so the list can
    /// never grow to an unbounded transfer no matter how many rows the series owns.
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
