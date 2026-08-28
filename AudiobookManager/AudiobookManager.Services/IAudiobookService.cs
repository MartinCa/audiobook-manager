using AudiobookManager.Domain;

namespace AudiobookManager.Services;
public interface IAudiobookService
{
    Audiobook ParseAudiobook(string filePath);

    Task<Audiobook> OrganizeAudiobook(Audiobook audiobook, Func<string, int, Task> progressAction);

    Task<Audiobook> InsertAudiobook(Audiobook audiobook);

    string GenerateLibraryPath(Audiobook audiobook);

    Task<TargetPathCollisionResult> CheckTargetPathCollision(Audiobook audiobook);

    Task<Audiobook> UpdateAudiobook(long id, Audiobook audiobook, Func<string, int, Task>? progressAction = null);

    Task<Audiobook?> GetAudiobookById(long id);
}
