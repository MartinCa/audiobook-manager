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
}
