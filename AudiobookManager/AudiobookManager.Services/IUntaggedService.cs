using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface IUntaggedService
{
    IEnumerable<AudiobookFileInfo> ScanInputDirectoryForAudiobookFiles();
}
