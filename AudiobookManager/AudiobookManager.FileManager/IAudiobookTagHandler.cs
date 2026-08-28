using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public interface IAudiobookTagHandler
{
    Audiobook ParseAudiobook(FileInfo fileInfo, bool includeCoverData = true);
    void SaveAudiobookTagsToFile(Audiobook audiobook, Action<float>? progressAction = null);
}
