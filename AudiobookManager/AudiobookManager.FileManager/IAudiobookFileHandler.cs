using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;

public interface IAudiobookFileHandler
{
    void RelocateAudiobook(Audiobook audiobook, string newFullPath, bool overwrite = false);
    void WriteMetadata(Audiobook audiobook);
    void WriteOpf(Audiobook audiobook);
    string? WriteCover(Audiobook audiobook);
    string? GetExistingCoverPath(string directoryPath, bool cleanupDuplicate);
    void MigrateSidecarFiles(string oldDirectory, string newDirectory);
    void RemoveSidecarFiles(string directoryPath);
    void RemoveDirIfEmpty(string directoryPath);
}
