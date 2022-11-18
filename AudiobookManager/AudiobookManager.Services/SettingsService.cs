using AudiobookManager.Database;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Services;

public interface ISettingsService
{
    Task<Domain.SeriesMapping> CreateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<Domain.SeriesMapping> UpdateSeriesMapping(Domain.SeriesMapping seriesMapping);
    Task<IList<Domain.SeriesMapping>> GetSeriesMappings();
    Task<Domain.SeriesMapping?> GetSeriesMapping(long id);
    Task DeleteSeriesMapping(long id);
}

public class SettingsService : ISettingsService
{
    private readonly DatabaseContext _db;

    public SettingsService(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<Domain.SeriesMapping> CreateSeriesMapping(Domain.SeriesMapping seriesMapping)
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

    public async Task<Domain.SeriesMapping?> GetSeriesMapping(long id)
    {
        var entity = await _db.SeriesMappings.SingleOrDefaultAsync(x => x.Id == id);
        if (entity == default)
        {
            return null;
        }

        return ToDomain(entity);
    }

    public async Task<IList<Domain.SeriesMapping>> GetSeriesMappings()
    {
        var dbModels = await _db.SeriesMappings.ToListAsync();
        return dbModels.Select(ToDomain).ToList();
    }

    public async Task<Domain.SeriesMapping> UpdateSeriesMapping(Domain.SeriesMapping seriesMapping)
    {
        var entity = ToDb(seriesMapping);

        _db.SeriesMappings.Update(entity);
        await _db.SaveChangesAsync();

        return ToDomain(entity);
    }

    private static Domain.SeriesMapping ToDomain(Database.Models.SeriesMapping dbModel) => new Domain.SeriesMapping(dbModel.Id, dbModel.Regex, dbModel.MappedSeries, dbModel.WarnAboutPart);

    private static Database.Models.SeriesMapping ToDb(Domain.SeriesMapping domainModel) => new Database.Models.SeriesMapping(domainModel.Id ?? default, domainModel.Regex, domainModel.MappedSeries, domainModel.WarnAboutParth);

}
