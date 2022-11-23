namespace AudiobookManager.Services;

public interface ISettingsService
{
    Task<Domain.SeriesMapping> CreateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<Domain.SeriesMapping> UpdateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<IList<Domain.SeriesMapping>> GetSeriesMappings();
    Task<Domain.SeriesMapping?> GetSeriesMapping(long id);
    Task DeleteSeriesMapping(long id);
}
