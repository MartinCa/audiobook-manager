using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookManager.FileManager;

public class FileOperations : IFileOperations
{
    private readonly ILogger<FileOperations> _logger;

    public FileOperations(ILogger<FileOperations>? logger = null)
    {
        _logger = logger ?? NullLogger<FileOperations>.Instance;
    }

    public void WriteAllText(string path, string content, string? reason = null)
    {
        var exists = File.Exists(path);
        File.WriteAllText(path, content);

        if (exists)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Overwrote file '{FilePath}' ({Length} characters) (reason: {Reason})", path, content.Length, reason);
            }
            else
            {
                _logger.LogInformation("Overwrote file '{FilePath}' ({Length} characters)", path, content.Length);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Created file '{FilePath}' ({Length} characters) (reason: {Reason})", path, content.Length, reason);
            }
            else
            {
                _logger.LogInformation("Created file '{FilePath}' ({Length} characters)", path, content.Length);
            }
        }
    }

    public void WriteAllBytes(string path, byte[] bytes, string? reason = null)
    {
        var exists = File.Exists(path);
        File.WriteAllBytes(path, bytes);

        if (exists)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Overwrote file '{FilePath}' ({Bytes} bytes) (reason: {Reason})", path, bytes.Length, reason);
            }
            else
            {
                _logger.LogInformation("Overwrote file '{FilePath}' ({Bytes} bytes)", path, bytes.Length);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Created file '{FilePath}' ({Bytes} bytes) (reason: {Reason})", path, bytes.Length, reason);
            }
            else
            {
                _logger.LogInformation("Created file '{FilePath}' ({Bytes} bytes)", path, bytes.Length);
            }
        }
    }

    public void DeleteFile(string path, string? reason = null)
    {
        File.Delete(path);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            _logger.LogInformation("Deleted file '{FilePath}' (reason: {Reason})", path, reason);
        }
        else
        {
            _logger.LogInformation("Deleted file '{FilePath}'", path);
        }
    }

    public bool DeleteFileIfExists(string path, string? reason = null)
    {
        if (File.Exists(path))
        {
            DeleteFile(path, reason);
            return true;
        }

        return false;
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false, string? reason = null)
    {
        File.Move(sourcePath, destinationPath, overwrite);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            _logger.LogInformation(
                "Moved file from '{SourcePath}' to '{DestinationPath}' (overwrite: {Overwrite}) (reason: {Reason})",
                sourcePath, destinationPath, overwrite, reason);
        }
        else
        {
            _logger.LogInformation(
                "Moved file from '{SourcePath}' to '{DestinationPath}' (overwrite: {Overwrite})",
                sourcePath, destinationPath, overwrite);
        }
    }

    public void CreateDirectory(string path, string? reason = null)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Created directory '{DirectoryPath}' (reason: {Reason})", path, reason);
            }
            else
            {
                _logger.LogInformation("Created directory '{DirectoryPath}'", path);
            }
        }
    }

    public void DeleteDirectory(string path, bool recursive = false, string? reason = null)
    {
        Directory.Delete(path, recursive);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            _logger.LogInformation(
                "Deleted directory '{DirectoryPath}' (recursive: {Recursive}) (reason: {Reason})",
                path, recursive, reason);
        }
        else
        {
            _logger.LogInformation(
                "Deleted directory '{DirectoryPath}' (recursive: {Recursive})",
                path, recursive);
        }
    }

    public bool DeleteDirectoryIfExists(string path, bool recursive = false, string? reason = null)
    {
        if (Directory.Exists(path))
        {
            DeleteDirectory(path, recursive, reason);
            return true;
        }

        return false;
    }

    public bool DeleteDirectoryIfEmpty(string path, string? reason = null)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation("Deleted empty directory '{DirectoryPath}' (reason: {Reason})", path, reason);
            }
            else
            {
                _logger.LogInformation("Deleted empty directory '{DirectoryPath}'", path);
            }

            return true;
        }

        return false;
    }
}
