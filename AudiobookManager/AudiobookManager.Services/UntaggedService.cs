using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

public class UntaggedService : IUntaggedService
{
    public IEnumerable<AudiobookFileInfo> ScanInputDirectoryForAudiobookFiles()
    {
        return FileScanner.ScanDirectoryForAudiobookFiles("C:\\tools\\abtest\\untagged");
    }
}