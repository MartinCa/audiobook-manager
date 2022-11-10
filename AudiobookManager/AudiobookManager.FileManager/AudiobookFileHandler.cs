using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public static class AudiobookFileHandler
{
    public static void RelocateAudiobook(Audiobook audiobook, string newFullPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath));
        File.Move(audiobook.FileInfo.FullPath, newFullPath);
    }

    public static void WriteMetadata(Audiobook audiobook)
    {
        var directoryPath = Path.GetDirectoryName(audiobook.FileInfo.FullPath);

        if (!string.IsNullOrEmpty(audiobook.Description))
        {
            MakeMetadataFile(directoryPath, "desc.txt", audiobook.Description);
        }
        if (audiobook.Narrators.Any())
        {
            MakeMetadataFile(directoryPath, "reader.txt", string.Join(", ", audiobook.Narrators.Select(x => x.Name)));
        }
        if (audiobook.Cover is not null)
        {
            var coverExtension = GetMimeFileExt(audiobook.Cover.MimeType);
            var fileName = Path.Join(directoryPath, $"cover{coverExtension}");
            using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            fs.Write(Convert.FromBase64String(audiobook.Cover.Base64Data));
        }
    }

    public static void RemoveDirIfEmpty(string directoryPath)
    {
        if (Directory.Exists(directoryPath) && !Directory.GetFiles(directoryPath).Any())
        {
            Directory.Delete(directoryPath);
        }
    }

    private static void MakeMetadataFile(string directoryPath, string fileName, string content)
    {
        var filePath = Path.Join(directoryPath, fileName);
        File.WriteAllText(filePath, content);
    }

    private static string GetMimeFileExt(string mimeType)
    {
        return mimeType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => throw new Exception($"Unsupported mime type {mimeType}")
        };
    }
}
