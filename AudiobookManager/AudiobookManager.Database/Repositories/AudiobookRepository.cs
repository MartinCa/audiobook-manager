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
        var lowerQuery = query.ToLower();

        var dbQuery = _db.Audiobooks
            .Include(a => a.Authors)
            .Include(a => a.Narrators)
            .Include(a => a.Genres)
            .Where(a =>
                (a.BookName != null && a.BookName.ToLower().Contains(lowerQuery)) ||
                (a.Subtitle != null && a.Subtitle.ToLower().Contains(lowerQuery)) ||
                (a.Description != null && a.Description.ToLower().Contains(lowerQuery)) ||
                (a.Series != null && a.Series.ToLower().Contains(lowerQuery)) ||
                a.Authors.Any(p => p.Name.ToLower().Contains(lowerQuery))
            )
            .OrderBy(a => a.BookName);

        var total = await dbQuery.CountAsync();
        var items = await dbQuery.Skip(offset).Take(limit).ToListAsync();
        return (items, total);
    }

    public async Task<List<Person>> GetAllAuthorsAsync()
    {
        return await _db.Persons
            .Include(p => p.BooksAuthored)
            .Where(p => p.BooksAuthored.Any())
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Person?> GetAuthorWithBooksAsync(long authorId)
    {
        return await _db.Persons
            .Include(p => p.BooksAuthored)
                .ThenInclude(a => a.Authors)
            .Include(p => p.BooksAuthored)
                .ThenInclude(a => a.Narrators)
            .Include(p => p.BooksAuthored)
                .ThenInclude(a => a.Genres)
            .FirstOrDefaultAsync(p => p.Id == authorId);
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
}
