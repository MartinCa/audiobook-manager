using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class FileService : IFileService
{
    private readonly AudiobookManagerSettings _settings;

    public FileService(IOptions<AudiobookManagerSettings> settings)
    {
        _settings = settings.Value;
    }

    public void DeleteDirectory(string directoryPath)
    {
        var dir = Path.HasExtension(directoryPath) ? Path.GetDirectoryName(directoryPath) : directoryPath;

        if (dir is null)
        {
            throw new ArgumentException(nameof(directoryPath), "Could not get directory");
        }

        Directory.Delete(dir, true);
    }
    public IList<AudiobookFileInfo> GetDirectoryContents(string directoryPath)
    {
        var dir = Path.HasExtension(directoryPath) ? Path.GetDirectoryName(directoryPath) : directoryPath;

        if (dir is null)
        {
            throw new ArgumentException(nameof(directoryPath), "Could not get directory");
        }

        return FileScanner.ScanDirectoryForFiles(dir);
    }

    public IEnumerable<AudiobookFileInfo> ScanInputDirectoryForAudiobookFiles()
    {
        return FileScanner.ScanDirectoryForFiles(_settings.AudiobookImportPath, (fileInfo) => AudiobookTagHandler.IsSupported(fileInfo));
    }
}
