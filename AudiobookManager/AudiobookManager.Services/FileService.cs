using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class FileService : IFileService
{
    private readonly IFileOperations _fileOperations;
    private readonly IAudiobookFileHandler _fileHandler;
    private readonly AudiobookManagerSettings _settings;

    public FileService(
        IFileOperations fileOperations,
        IAudiobookFileHandler fileHandler,
        IOptions<AudiobookManagerSettings> settings)
    {
        _fileOperations = fileOperations;
        _fileHandler = fileHandler;
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
                _fileOperations.DeleteFile(directoryPath, "user requested deletion of file in root directory");
                return;
            }

            ValidateNotRoot(parentDir);
            _fileOperations.DeleteDirectory(parentDir, true, "user requested deletion of file parent directory");
            return;
        }

        ValidateNotRoot(directoryPath);
        _fileOperations.DeleteDirectory(directoryPath, true, "user requested deletion of directory");
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

    public string? GetCoverPath(string filePath)
    {
        ValidatePathWithinAllowedBases(filePath);

        var directoryPath = Path.GetDirectoryName(filePath);
        if (directoryPath is null)
        {
            throw new ArgumentException("Could not get directory", nameof(filePath));
        }

        // Read-only lookup for a discovered (not yet DB-tracked) file's preview - never the
        // WriteCover path that owns and mutates the directory, so never clean up a duplicate
        // cover.png/.jpg pair here.
        return _fileHandler.GetExistingCoverPath(directoryPath, cleanupDuplicate: false);
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
