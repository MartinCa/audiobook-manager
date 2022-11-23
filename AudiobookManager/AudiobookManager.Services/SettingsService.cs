using AudiobookManager.Database.Repositories;
using AudiobookManager.Services.MappingExtensions;

namespace AudiobookManager.Services;

public class SettingsService : ISettingsService
{
    private readonly ISeriesMappingRepository _seriesMappingRepository;

    public SettingsService(ISeriesMappingRepository seriesMappingRepository)
    {
        _seriesMappingRepository= seriesMappingRepository;
    }

    public async Task<Domain.SeriesMapping> CreateSeriesMapping(Domain.SeriesMapping seriesMapping)
    {
        var dbModel = seriesMapping.ToDb();
        dbModel = await _seriesMappingRepository.CreateSeriesMapping(dbModel);
        return dbModel.ToDomain();
    }

    public async Task DeleteSeriesMapping(long id)
    {
        await _seriesMappingRepository.DeleteSeriesMapping(id);
    }

    public async Task<Domain.SeriesMapping?> GetSeriesMapping(long id)
    {
        var dbModel = await _seriesMappingRepository.GetSeriesMapping(id);

        return dbModel?.ToDomain();
    }

    public async Task<IList<Domain.SeriesMapping>> GetSeriesMappings()
    {
        var dbModels = await _seriesMappingRepository.GetSeriesMappings();
        return dbModels.Select(SeriesMappingMapping.ToDomain).ToList();
    }

    public async Task<Domain.SeriesMapping> UpdateSeriesMapping(Domain.SeriesMapping seriesMapping)
    {
        var dbModel = seriesMapping.ToDb();

        dbModel = await _seriesMappingRepository.UpdateSeriesMapping(dbModel);

        return dbModel.ToDomain();
    }

}
