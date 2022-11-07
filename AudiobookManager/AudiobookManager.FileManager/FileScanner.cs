using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public class FileScanner
{
    public static List<AudiobookFileInfo> ScanDirectoryForFiles(string path, Func<FileInfo, bool>? fileFilter = null)
    {
        var result = new List<AudiobookFileInfo>();

        foreach (string sPath in Directory.GetFiles(path))
        {
            var fileInfo = new FileInfo(sPath);
            if (fileFilter is null || fileFilter(fileInfo))
            {
                result.Add(new AudiobookFileInfo(fileInfo));
            }
        }

        foreach (string sPath in Directory.GetDirectories(path))
        {
            result.AddRange(ScanDirectoryForFiles(sPath));
        }

        return result;
    }
}
