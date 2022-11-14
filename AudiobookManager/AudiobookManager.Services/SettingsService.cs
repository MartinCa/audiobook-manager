using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Services;

public interface ISettingsService
{
    Task<SeriesMapping> CreateSeriesMapping(SeriesMapping seriesMapping);
    Task<SeriesMapping> UpdateSeriesMapping(SeriesMapping seriesMapping);
    Task<IList<SeriesMapping>> GetSeriesMappings();
    Task<SeriesMapping?> GetSeriesMapping(long id);
    Task DeleteSeriesMapping(long id);
}

public class SettingsService : ISettingsService
{
    private readonly DatabaseContext _db;

    public SettingsService(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<SeriesMapping> CreateSeriesMapping(SeriesMapping seriesMapping)
    {
        var dbModel = ToDb(seriesMapping);
        _db.SeriesMappings.Add(dbModel);
        await _db.SaveChangesAsync();
        return ToDomain(dbModel);
    }

    public async Task DeleteSeriesMapping(long id)
    {
        var entity = _db.SeriesMappings.SingleOrDefault(x => x.Id == id);
        if (entity != default)
        {
            _db.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<SeriesMapping?> GetSeriesMapping(long id)
    {
        var entity = await _db.SeriesMappings.SingleOrDefaultAsync(x => x.Id == id);
        if (entity == default)
        {
            return null;
        }

        return ToDomain(entity);
    }

    public async Task<IList<SeriesMapping>> GetSeriesMappings()
    {
        var dbModels = await _db.SeriesMappings.ToListAsync();
        return dbModels.Select(ToDomain).ToList();
    }

    public async Task<SeriesMapping> UpdateSeriesMapping(SeriesMapping seriesMapping)
    {
        var entity = ToDb(seriesMapping);

        _db.SeriesMappings.Update(entity);
        await _db.SaveChangesAsync();

        return ToDomain(entity);
    }

    private static SeriesMapping ToDomain(SeriesMappingDb dbModel) => new SeriesMapping(dbModel.Id, dbModel.Regex, dbModel.MappedSeries, dbModel.WarnAboutPart);

    private static SeriesMappingDb ToDb(SeriesMapping domainModel) => new SeriesMappingDb(domainModel.Id ?? default, domainModel.Regex, domainModel.MappedSeries, domainModel.WarnAboutParth);

}
