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
        ValidatePathWithinAllowedBases(directoryPath);

        if (File.Exists(directoryPath))
        {
            var parentDir = Path.GetDirectoryName(directoryPath);
            if (parentDir is null)
            {
                throw new ArgumentException("Could not get directory", nameof(directoryPath));
            }

            if (AudiobookFileHandler.PathsEqual(parentDir, _settings.AudiobookImportPath) ||
                AudiobookFileHandler.PathsEqual(parentDir, _settings.AudiobookLibraryPath))
            {
                File.Delete(directoryPath);
                return;
            }

            ValidateNotRoot(parentDir);
            Directory.Delete(parentDir, true);
            return;
        }

        ValidateNotRoot(directoryPath);
        Directory.Delete(directoryPath, true);
    }

    public IList<AudiobookFileInfo> GetDirectoryContents(string directoryPath)
    {
        ValidatePathWithinAllowedBases(directoryPath);

        if (File.Exists(directoryPath))
        {
            var parentDir = Path.GetDirectoryName(directoryPath);
            if (parentDir is null)
            {
                throw new ArgumentException("Could not get directory", nameof(directoryPath));
            }

            if (AudiobookFileHandler.PathsEqual(parentDir, _settings.AudiobookImportPath) ||
                AudiobookFileHandler.PathsEqual(parentDir, _settings.AudiobookLibraryPath))
            {
                var fileInfo = new FileInfo(directoryPath);
                return new List<AudiobookFileInfo>
                {
                    new(fileInfo.FullName, fileInfo.Name, fileInfo.Length)
                };
            }

            ValidateNotRoot(parentDir);
            return FileScanner.ScanDirectoryForFiles(parentDir);
        }

        ValidateNotRoot(directoryPath);
        return FileScanner.ScanDirectoryForFiles(directoryPath);
    }

    private void ValidateNotRoot(string path)
    {
        if (AudiobookFileHandler.PathsEqual(path, _settings.AudiobookImportPath))
        {
            throw new InvalidOperationException("Cannot delete or inspect the entire import root directory");
        }

        if (AudiobookFileHandler.PathsEqual(path, _settings.AudiobookLibraryPath))
        {
            throw new InvalidOperationException("Cannot delete or inspect the entire library root directory");
        }
    }

    private void ValidatePathWithinAllowedBases(string path)
    {
        if (!AudiobookFileHandler.PathStartsWith(path, _settings.AudiobookImportPath) &&
            !AudiobookFileHandler.PathStartsWith(path, _settings.AudiobookLibraryPath))
        {
            throw new UnauthorizedAccessException($"Access to path '{path}' is not allowed");
        }
    }

    public IEnumerable<AudiobookFileInfo> ScanInputDirectoryForAudiobookFiles()
    {
        return FileScanner.ScanDirectoryForFiles(_settings.AudiobookImportPath, (fileInfo) => AudiobookTagHandler.IsSupported(fileInfo));
    }
}
