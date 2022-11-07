using System.Text.Json.Serialization;

namespace AudiobookManager.Domain;

public class AudiobookFileInfo
{
    public string FullPath { get; set; }
    public string FileName { get; set; }
    public long SizeInBytes { get; set; }

    [JsonConstructor]
    public AudiobookFileInfo(string fullPath, string fileName, long sizeInBytes)
    {
        FullPath = fullPath;
        FileName = fileName;
        SizeInBytes = sizeInBytes;
    }

    public AudiobookFileInfo(FileInfo fileInfo)
    {
        FullPath = fileInfo.FullName;
        FileName = fileInfo.Name;
        SizeInBytes = fileInfo.Length;
    }
}
