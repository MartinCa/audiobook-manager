using AudiobookManager.Domain;

namespace AudiobookManager.Services;
public interface IAudiobookService
{
    Audiobook ParseAudiobook(string filePath, bool includeCoverData = true);

    Task<Audiobook> OrganizeAudiobook(Audiobook audiobook, Func<string, int, Task> progressAction);
    Task<Audiobook> OrganizeAudiobook(Audiobook audiobook, Func<string, int, Task> progressAction, bool metadataAppliedFromSearch);

    Task<Audiobook> InsertAudiobook(Audiobook audiobook);

    /// <summary>The library path for <paramref name="audiobook"/> under the given narrator-in-path setting.</summary>
    string GenerateLibraryPath(Audiobook audiobook, int maxNarratorsInPath);

    /// <summary>The library path for <paramref name="audiobook"/> under the library's current narrator-in-path setting.</summary>
    Task<string> GenerateLibraryPathAsync(Audiobook audiobook);

    Task<TargetPathCollisionResult> CheckTargetPathCollision(Audiobook audiobook);

    Task<Audiobook> UpdateAudiobook(long id, Audiobook audiobook, Func<string, int, Task>? progressAction = null);
    Task<Audiobook> UpdateAudiobook(long id, Audiobook audiobook, Func<string, int, Task>? progressAction, bool metadataAppliedFromSearch);

    /// <summary>
    /// Bookkeeping-only column write: "metadata was last applied/checked against an online
    /// source at <paramref name="whenUtc"/>". Never touches tags, files or sidecars.
    /// </summary>
    Task MarkMetadataRefreshedAsync(long id, DateTime whenUtc);

    Task<Audiobook?> GetAudiobookById(long id);

    Task DeleteAudiobook(long id);
}
