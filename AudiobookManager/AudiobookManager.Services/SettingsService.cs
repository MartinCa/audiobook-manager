using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface ISettingsService
{
    SeriesMapping CreateSeriesMapping(SeriesMapping seriesMapping);
    SeriesMapping UpdateSeriesMapping(SeriesMapping seriesMapping);
    IList<SeriesMapping> GetSeriesMappings();
    SeriesMapping? GetSeriesMapping(long id);
    void DeleteSeriesMapping(long id);
}

public class SettingsService : ISettingsService
{
    private readonly DatabaseContext _db;

    public SettingsService(DatabaseContext db)
    {
        _db = db;
    }

    public SeriesMapping CreateSeriesMapping(SeriesMapping seriesMapping)
    {
        var dbModel = ToDb(seriesMapping);
        _db.SeriesMappings.Add(dbModel);
        _db.SaveChanges();
        return ToDomain(dbModel);
    }

    public void DeleteSeriesMapping(long id)
    {
        var entity = _db.SeriesMappings.SingleOrDefault(x => x.Id == id);
        if (entity != default)
        {
            _db.Remove(entity);
            _db.SaveChanges();
        }
    }

    public SeriesMapping? GetSeriesMapping(long id)
    {
        var entity = _db.SeriesMappings.SingleOrDefault(x => x.Id == id);
        if (entity == default)
        {
            return null;
        }

        return ToDomain(entity);
    }

    public IList<SeriesMapping> GetSeriesMappings()
    {
        return _db.SeriesMappings.Select(x => ToDomain(x)).ToList();
    }

    public SeriesMapping UpdateSeriesMapping(SeriesMapping seriesMapping)
    {
        var entity = ToDb(seriesMapping);

        _db.SeriesMappings.Update(entity);
        _db.SaveChanges();

        return ToDomain(entity);
    }

    private static SeriesMapping ToDomain(SeriesMappingDb dbModel) => new SeriesMapping(dbModel.Id, dbModel.Regex, dbModel.MappedSeries, dbModel.WarnAboutPart);

    private static SeriesMappingDb ToDb(SeriesMapping domainModel) => new SeriesMappingDb(domainModel.Id ?? default, domainModel.Regex, domainModel.MappedSeries, domainModel.WarnAboutParth);

}
