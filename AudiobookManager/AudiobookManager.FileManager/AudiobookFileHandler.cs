using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public static class AudiobookFileHandler
{
    private const string _replacementInvalidPathSeparator = "_";
    private const string _replaceInvalidPathOrFileNameCharacter = "";
    private const char _preferredDirectorySeparatorChar = '/';
    private static char[] _systemDirectorySeparators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
    private static readonly string[] _sidecarFileNames = new[] { "desc.txt", "reader.txt", "cover.jpg", "cover.png" };

    // Windows and macOS file systems are case-insensitive while Linux is case-sensitive, so two
    // paths differing only in case refer to the same file on the former but not the latter.
    public static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Comparer form of <see cref="PathComparison"/>, for hash sets/dictionaries keyed by path.
    /// Any collection that decides "have I already seen this path?" must use this rather than the
    /// default (always case-sensitive) comparer - see the path-comparison invariant in CLAUDE.md.
    /// </summary>
    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static bool PathsEqual(string pathA, string pathB) =>
        string.Equals(Path.GetFullPath(pathA), Path.GetFullPath(pathB), PathComparison);

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="prefix"/> itself or sits underneath it.
    /// The boundary check matters: a bare string StartsWith would report "/data/library-backup"
    /// as being inside "/data/library", which would let a caller relying on this for access
    /// control (FileService.ValidatePathWithinAllowedBases) reach a sibling directory.
    /// </summary>
    public static bool PathStartsWith(string path, string prefix)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(prefix));

        if (string.Equals(fullPath, fullPrefix, PathComparison))
        {
            return true;
        }

        return fullPath.Length > fullPrefix.Length
            && fullPath.StartsWith(fullPrefix, PathComparison)
            && _systemDirectorySeparators.Contains(fullPath[fullPrefix.Length]);
    }

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

    public static string? WriteCover(Audiobook audiobook)
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
        if (Directory.Exists(directoryPath) && !Directory.GetFiles(directoryPath).Any() && !Directory.GetDirectories(directoryPath).Any())
        {
            Directory.Delete(directoryPath);
        }
    }

    public static void RemoveSidecarFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var fileName in _sidecarFileNames)
        {
            var filePath = JoinPaths(directoryPath, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
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
    {
        var invalidChars = new HashSet<char>(charsToReplace);
        if (!inputString.Any(invalidChars.Contains))
        {
            return inputString;
        }

        var builder = new System.Text.StringBuilder(inputString.Length);
        foreach (var c in inputString)
        {
            if (invalidChars.Contains(c))
            {
                builder.Append(replacementString);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

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
