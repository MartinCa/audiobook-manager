namespace AudiobookManager.FileManager;

public interface IFileOperations
{
    void WriteAllText(string path, string content, string? reason = null);
    void WriteAllBytes(string path, byte[] bytes, string? reason = null);
    void DeleteFile(string path, string? reason = null);
    bool DeleteFileIfExists(string path, string? reason = null);
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false, string? reason = null);
    void CreateDirectory(string path, string? reason = null);
    void DeleteDirectory(string path, bool recursive = false, string? reason = null);
    bool DeleteDirectoryIfExists(string path, bool recursive = false, string? reason = null);
    bool DeleteDirectoryIfEmpty(string path, string? reason = null);
}
