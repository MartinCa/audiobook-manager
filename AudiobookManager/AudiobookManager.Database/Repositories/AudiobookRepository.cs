using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class AudiobookRepository : IAudiobookRepository
{
    private readonly DatabaseContext _db;

    public AudiobookRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<Audiobook> InsertAudiobook(Audiobook audiobook)
    {
        _db.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    public async Task<HashSet<string>> GetAllFilePathsAsync()
    {
        var paths = await _db.Audiobooks.Select(a => a.FileInfoFullPath).ToListAsync();
        return paths.ToHashSet();
    }

    public async Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset)
    {
        var query = _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .OrderBy(a => a.BookName);

        var total = await query.CountAsync();
        var items = await query.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<(List<Audiobook> Items, int Total)> SearchAsync(string query, int limit, int offset)
    {
        var pattern = $"%{query}%";

        var dbQuery = _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .Where(a =>
                (a.BookName != null && EF.Functions.Like(a.BookName, pattern)) ||
                (a.Subtitle != null && EF.Functions.Like(a.Subtitle, pattern)) ||
                (a.Description != null && EF.Functions.Like(a.Description, pattern)) ||
                (a.Series != null && EF.Functions.Like(a.Series, pattern)) ||
                a.Authors.Any(p => EF.Functions.Like(p.Name, pattern))
            )
            .OrderBy(a => a.BookName);

        var total = await dbQuery.CountAsync();
        var items = await dbQuery.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId)
    {
        var query = _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .Where(a => a.Series == seriesName);

        if (authorId.HasValue)
        {
            query = query.Where(a => a.Authors.Any(p => p.Id == authorId.Value));
        }

        return await query.OrderBy(a => a.SeriesPart).ToListAsync();
    }

    public async Task<Audiobook?> GetByIdWithIncludesAsync(long id)
    {
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<Audiobook>> GetAllWithIncludesAsync()
    {
        return await _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .OrderBy(a => a.BookName)
            .ToListAsync();
    }

    public async Task UpdateFilePathAsync(long id, string newFullPath, string newFileName)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            audiobook.FileInfoFullPath = newFullPath;
            audiobook.FileInfoFileName = newFileName;
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateCoverFilePathAsync(long id, string? coverFilePath)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            audiobook.CoverFilePath = coverFilePath;
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteAudiobookAsync(long id)
    {
        var audiobook = await _db.Audiobooks.FindAsync(id);
        if (audiobook != null)
        {
            _db.Audiobooks.Remove(audiobook);
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateAudiobookAsync(Audiobook audiobook)
    {
        _db.Audiobooks.Update(audiobook);
        await _db.SaveChangesAsync();
    }
}
