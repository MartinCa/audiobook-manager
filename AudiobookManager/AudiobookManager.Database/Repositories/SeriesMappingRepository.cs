using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class SeriesMappingRepository : ISeriesMappingRepository
{
    private readonly DatabaseContext _db;

    public SeriesMappingRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<SeriesMapping> CreateSeriesMapping(SeriesMapping seriesMapping)
    {
        _db.SeriesMappings.Add(seriesMapping);
        await _db.SaveChangesAsync();

        return seriesMapping;
    }

    public async Task DeleteSeriesMapping(long id)
    {
        var entity = await _db.SeriesMappings.FindAsync(id);
        if (entity != null)
        {
            _db.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<SeriesMapping?> GetSeriesMapping(long id)
    {
        return await _db.SeriesMappings.FindAsync(id);
    }

    public async Task<IList<SeriesMapping>> GetSeriesMappings()
    {
        return await _db.SeriesMappings.ToListAsync();
    }

    public async Task<SeriesMapping> UpdateSeriesMapping(SeriesMapping seriesMapping)
    {
        _db.Update(seriesMapping);
        await _db.SaveChangesAsync();

        return seriesMapping;
    }
}
