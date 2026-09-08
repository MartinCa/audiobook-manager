using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface ISeriesMappingRepository
{
    Task<SeriesMapping> CreateSeriesMapping(SeriesMapping seriesMapping);
    Task DeleteSeriesMapping(long id);
    Task<SeriesMapping?> GetSeriesMapping(long id);

    /// <summary>
    /// The series mappings already grouped by target series name, for the Settings page. The
    /// grouping happens here (server-side) because the client used to fetch the flat table and
    /// reduce it itself; <paramref name="search"/> folds accents over both the regex pattern and
    /// the target name. Returns the groups and the total number of matching mapping rows.
    /// </summary>
    Task<(List<(string MappedSeries, List<SeriesMapping> Items)> Groups, int Total)> GetSeriesMappingGroupsAsync(string? search);
    Task<SeriesMapping> UpdateSeriesMapping(SeriesMapping seriesMapping);
}
