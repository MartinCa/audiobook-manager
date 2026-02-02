using AudiobookManager.Domain;

namespace AudiobookManager.Services;
public interface IAudiobookService
{
    Audiobook ParseAudiobook(string filePath);

    Task<Audiobook> OrganizeAudiobook(Audiobook audiobook, Func<string, int, Task> progressAction);

    Task<Audiobook> InsertAudiobook(Audiobook audiobook);

    string GenerateLibraryPath(Audiobook audiobook);

    Task<Audiobook> UpdateAudiobook(long id, Audiobook audiobook);

    Task<Audiobook?> GetAudiobookById(long id);
}
