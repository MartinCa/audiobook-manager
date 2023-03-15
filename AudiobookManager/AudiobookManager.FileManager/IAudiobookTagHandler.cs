using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public interface IAudiobookTagHandler
{
    Audiobook ParseAudiobook(FileInfo fileInfo);
    void SaveAudiobookTagsToFile(Audiobook audiobook, Action<float>? progressAction = null);
}
