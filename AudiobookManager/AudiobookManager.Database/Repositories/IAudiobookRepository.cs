using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IAudiobookRepository
{
    Task<Audiobook> InsertAudiobook(Audiobook audiobook);
}
