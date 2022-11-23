using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface ISeriesMappingRepository
{
    Task<SeriesMapping> CreateSeriesMapping(SeriesMapping seriesMapping);
    Task DeleteSeriesMapping(long id);
    Task<SeriesMapping?> GetSeriesMapping(long id);
    Task<IList<SeriesMapping>> GetSeriesMappings();
    Task<SeriesMapping> UpdateSeriesMapping(SeriesMapping seriesMapping);
}
