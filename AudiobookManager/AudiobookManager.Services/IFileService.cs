using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface IFileService
{
    IEnumerable<AudiobookFileInfo> ScanInputDirectoryForAudiobookFiles();
    public IList<AudiobookFileInfo> GetDirectoryContents(string directoryPath);
    public void DeleteDirectory(string directoryPath);
}
