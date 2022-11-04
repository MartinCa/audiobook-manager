using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public class FileScanner
{
    public static List<AudiobookFileInfo> ScanDirectoryForAudiobookFiles(string path)
    {
        var result = new List<AudiobookFileInfo>();

        foreach (string sPath in Directory.GetFiles(path))
        {
            var fileInfo = new FileInfo(sPath);
            if (AudiobookParser.IsSupported(fileInfo))
            {
                result.Add(new AudiobookFileInfo(fileInfo));
            }
        }

        foreach (string sPath in Directory.GetDirectories(path))
        {
            result.AddRange(ScanDirectoryForAudiobookFiles(sPath));
        }

        return result;
    }
}
