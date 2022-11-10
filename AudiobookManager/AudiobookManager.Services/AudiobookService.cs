using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;
public class AudiobookService : IAudiobookService
{
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly AudiobookManagerSettings _settings;

    public AudiobookService(IAudiobookTagHandler tagHandler, IOptions<AudiobookManagerSettings> settings)
    {
        _tagHandler = tagHandler;
        _settings = settings.Value;
    }

    public Audiobook ParseAudiobook(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        return _tagHandler.ParseAudiobook(fileInfo);
    }

    public Audiobook OrganizeAudiobook(Audiobook audiobook)
    {
        if (string.IsNullOrEmpty(audiobook.FileInfo?.FullPath))
        {
            throw new Exception("No full path for audiobook");
        }

        var oldDirectory = Path.GetDirectoryName(audiobook.FileInfo.FullPath);

        _tagHandler.SaveAudiobookTagsToFile(audiobook);

        var newRelativePath = _tagHandler.GenerateRelativeAudiobookPath(audiobook);
        var newFullPath = Path.Join(_settings.AudiobookLibraryPath, newRelativePath);

        if (File.Exists(newFullPath))
        {
            throw new Exception($"File '{newFullPath}' already exists");
        }

        AudiobookFileHandler.RelocateAudiobook(audiobook, newFullPath);
        var newParsed = ParseAudiobook(newFullPath);

        AudiobookFileHandler.WriteMetadata(newParsed);

        AudiobookFileHandler.RemoveDirIfEmpty(oldDirectory);

        return newParsed;
    }


}
