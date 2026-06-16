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

        ValidatePathWithinAllowedBases(dir);
        Directory.Delete(dir, true);
    }
    public IList<AudiobookFileInfo> GetDirectoryContents(string directoryPath)
    {
        var dir = Path.HasExtension(directoryPath) ? Path.GetDirectoryName(directoryPath) : directoryPath;

        if (dir is null)
        {
            throw new ArgumentException(nameof(directoryPath), "Could not get directory");
        }

        ValidatePathWithinAllowedBases(dir);
        return FileScanner.ScanDirectoryForFiles(dir);
    }

    private void ValidatePathWithinAllowedBases(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var importBase = Path.GetFullPath(_settings.AudiobookImportPath);
        var libraryBase = Path.GetFullPath(_settings.AudiobookLibraryPath);

        if (!fullPath.StartsWith(importBase, StringComparison.Ordinal) &&
            !fullPath.StartsWith(libraryBase, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Access to path '{path}' is not allowed");
        }
    }

    public IEnumerable<AudiobookFileInfo> ScanInputDirectoryForAudiobookFiles()
    {
        return FileScanner.ScanDirectoryForFiles(_settings.AudiobookImportPath, (fileInfo) => AudiobookTagHandler.IsSupported(fileInfo));
    }
}
