using AudiobookManager.Database.Models;

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
}
