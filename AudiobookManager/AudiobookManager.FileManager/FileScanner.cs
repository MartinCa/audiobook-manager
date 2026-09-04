using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public class FileScanner
{
    public static List<AudiobookFileInfo> ScanDirectoryForFiles(string path, Func<FileInfo, bool>? fileFilter = null)
    {
        var result = new List<AudiobookFileInfo>();
        var directories = new Stack<string>();
        directories.Push(path);

        // Iterative, single accumulating list with lazy Enumerate* calls instead of recursive
        // GetFiles/GetDirectories (which each materialize their own array) merged via AddRange
        // at every level - avoids O(depth) intermediate list allocations on deep/wide trees.
        while (directories.Count > 0)
        {
            var currentPath = directories.Pop();

            foreach (string sPath in Directory.EnumerateFiles(currentPath))
            {
                var fileInfo = new FileInfo(sPath);
                if (fileFilter is null || fileFilter(fileInfo))
                {
                    result.Add(new AudiobookFileInfo(fileInfo));
                }
            }

            foreach (string sPath in Directory.EnumerateDirectories(currentPath))
            {
                // Not into symlinks: a link back to an ancestor turns this walk into one that
                // never terminates, and the same file reached through two paths would be scanned
                // twice. See DirectoryWalk.
                if (!DirectoryWalk.IsLink(sPath))
                {
                    directories.Push(sPath);
                }
            }
        }

        return result;
    }
}
