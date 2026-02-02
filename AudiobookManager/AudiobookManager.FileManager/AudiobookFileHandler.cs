using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public static class AudiobookFileHandler
{
    private const string _replacementInvalidPathSeparator = "_";
    private const string _replaceInvalidPathOrFileNameCharacter = "";
    private const char _preferredDirectorySeparatorChar = '/';
    private static char[] _systemDirectorySeparators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

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
    }

    public static string WriteCover(Audiobook audiobook)
    {
        if (audiobook.Cover is not null)
        {
            var directoryPath = Path.GetDirectoryName(audiobook.FileInfo.FullPath);
            var coverExtension = GetMimeFileExt(audiobook.Cover.MimeType);
            var fileName = JoinPaths(directoryPath, $"cover{coverExtension}");
            using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
            fs.Write(Convert.FromBase64String(audiobook.Cover.Base64Data));

            return fileName;
        }

        return null;
    }

    public static void RemoveDirIfEmpty(string directoryPath)
    {
        if (Directory.Exists(directoryPath) && !Directory.GetFiles(directoryPath).Any())
        {
            Directory.Delete(directoryPath);
        }
    }

    public static string GenerateRelativeAudiobookPath(Audiobook audiobook)
    {
        if (audiobook.FileInfo is null)
        {
            throw new ArgumentNullException(nameof(audiobook), "FileInfo is null");
        }

        var fileName = $"{audiobook.Year} - {audiobook.BookName}";

        var pathParts = new List<string>();
        pathParts.Add(AudiobookTagHandler.GetStringFromListOfPersons(audiobook.Authors));
        if (!string.IsNullOrEmpty(audiobook.Series))
        {
            pathParts.Add(audiobook.Series);
            var seriesPart = !string.IsNullOrEmpty(audiobook.SeriesPart) ? $" {AudiobookTagHandler.PadSeriesPart(audiobook.SeriesPart)}" : "";
            var seriesDirectory = !string.IsNullOrEmpty(audiobook.SeriesPart) ? $"Book{seriesPart} - " : "";
            pathParts.Add($"{seriesDirectory}{audiobook.Year} - {audiobook.BookName}");

            fileName = $"{audiobook.Series}{seriesPart} - {fileName}";
        }
        else
        {
            pathParts.Add($"{audiobook.Year} - {audiobook.BookName}");
        }

        return CombinePathAndFilename(pathParts, fileName, Path.GetExtension(audiobook.FileInfo.FullPath));
    }

    public static string JoinPaths(string path1, string path2) => $"{GetSafeCompletePath(path1)}{GetDirectorySeparator()}{GetSafeCompletePath(path2)}";

    public static string CombinePathAndFilename(IEnumerable<string> pathParts, string fileName, string extension) =>
        GetSafeCombinedPath(pathParts.Concat(new[] { $"{fileName}{GetExtensionWithDot(extension)}" }));

    public static string GetSafeCombinedPath(IEnumerable<string> pathParts) =>
        pathParts.Aggregate(string.Empty, (acc, curr) => string.IsNullOrEmpty(acc) ? GetSafeFileName(curr) : acc + GetDirectorySeparator() + GetSafeFileName(curr));

    public static string GetSafeCompletePath(this string path)
        => path.ReplaceChars(Path.GetInvalidPathChars(), _replaceInvalidPathOrFileNameCharacter);

    public static string GetSafeFileName(this string fileName)
        => fileName.ReplaceCharsAndPathSeparators(Path.GetInvalidFileNameChars(), _replaceInvalidPathOrFileNameCharacter);

    public static char GetDirectorySeparator() => _systemDirectorySeparators.Contains(_preferredDirectorySeparatorChar) ? _preferredDirectorySeparatorChar : Path.DirectorySeparatorChar;

    private static string GetExtensionWithDot(this string extension) => extension.StartsWith('.') ? extension : $".{extension}";

    private static string ReplaceCharsAndPathSeparators(this string inputString, char[] charsToReplace, string replacementString) =>
        inputString.ReplacePathSeparators().ReplaceChars(charsToReplace, replacementString);

    private static string ReplaceChars(this string inputString, char[] charsToReplace, string replacementString)
        => charsToReplace.Aggregate(inputString, (acc, currentChar) => acc.Replace(currentChar.ToString(), replacementString));

    private static string ReplacePathSeparators(this string path)
        => path.ReplaceChars(_systemDirectorySeparators, _replacementInvalidPathSeparator);

    private static void MakeMetadataFile(string directoryPath, string fileName, string content)
    {
        var filePath = JoinPaths(directoryPath, fileName);
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
