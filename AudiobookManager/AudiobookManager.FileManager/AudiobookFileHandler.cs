using System.Xml.Linq;
using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;

public class AudiobookFileHandler : IAudiobookFileHandler
{
    private static readonly string[] _sidecarFileNames = new[] { "desc.txt", "reader.txt", "cover.jpg", "cover.png", "metadata.opf" };
    private static readonly string[] _coverExtensions = new[] { ".jpg", ".png" };

    private static readonly XNamespace _opfNamespace = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace _dcNamespace = "http://purl.org/dc/elements/1.1/";

    private readonly IFileOperations _fileOperations;
    private static readonly IFileOperations _defaultFileOperations = new FileOperations();

    public AudiobookFileHandler(IFileOperations fileOperations)
    {
        _fileOperations = fileOperations;
    }

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
        var fullPath = Path.GetFullPath(path);
        var fullPrefix = Path.GetFullPath(prefix);

        if (string.Equals(fullPath, fullPrefix, PathComparison))
        {
            return true;
        }

        // Require a separator at the boundary so a sibling cannot match. GetFullPath leaves a
        // trailing separator only on a root ("/" or "C:\"), where it is part of the path rather
        // than a suffix - appending a second one there would reject everything under it.
        if (!Path.EndsInDirectorySeparator(fullPrefix))
        {
            fullPrefix += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(fullPrefix, PathComparison);
    }

    public void RelocateAudiobook(Audiobook audiobook, string newFullPath, bool overwrite = false)
    {
        var targetDir = Path.GetDirectoryName(newFullPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            _fileOperations.CreateDirectory(targetDir, "audiobook relocation");
        }
        _fileOperations.MoveFile(audiobook.FileInfo.FullPath, newFullPath, overwrite, "audiobook relocation");
    }

    public static void RelocateAudiobookStatic(Audiobook audiobook, string newFullPath, bool overwrite = false) =>
        new AudiobookFileHandler(_defaultFileOperations).RelocateAudiobook(audiobook, newFullPath, overwrite);

    /// <summary>
    /// Writes the sidecars this application owns (desc.txt, reader.txt, metadata.opf) from the
    /// audiobook's tags.
    ///
    /// A field that is now empty removes its sidecar rather than leaving the previous one in
    /// place. These files are generated, not user-authored - the consistency check requires
    /// desc.txt to equal the Description tag byte for byte - so a leftover desc.txt after the
    /// description is cleared is simply wrong, and it is the file Audiobookshelf reads in
    /// preference to the m4b's own tag. Nothing rewrote or reported it, so the old text stayed
    /// on disk indefinitely.
    /// </summary>
    public void WriteMetadata(Audiobook audiobook)
    {
        var directoryPath = Path.GetDirectoryName(audiobook.FileInfo.FullPath)!;

        if (!string.IsNullOrEmpty(audiobook.Description))
        {
            MakeMetadataFile(directoryPath, "desc.txt", audiobook.Description);
        }
        else
        {
            _fileOperations.DeleteFileIfExists(JoinPaths(directoryPath, "desc.txt"), "empty description sidecar cleanup");
        }

        if (audiobook.Narrators.Any())
        {
            MakeMetadataFile(directoryPath, "reader.txt", string.Join(", ", audiobook.Narrators.Select(x => x.Name)));
        }
        else
        {
            _fileOperations.DeleteFileIfExists(JoinPaths(directoryPath, "reader.txt"), "empty narrators sidecar cleanup");
        }

        WriteOpf(audiobook);
    }

    public static void WriteMetadataStatic(Audiobook audiobook) =>
        new AudiobookFileHandler(_defaultFileOperations).WriteMetadata(audiobook);

    public void WriteOpf(Audiobook audiobook)
    {
        var directoryPath = Path.GetDirectoryName(audiobook.FileInfo.FullPath)!;
        MakeMetadataFile(directoryPath, "metadata.opf", BuildOpfContent(audiobook));
    }

    public static void WriteOpfStatic(Audiobook audiobook) =>
        new AudiobookFileHandler(_defaultFileOperations).WriteOpf(audiobook);

    /// <summary>
    /// Builds the metadata.opf sidecar content (Calibre/Audiobookshelf's standard OPF format,
    /// sitting above the m4b's embedded tags in Audiobookshelf's own metadata precedence). Used
    /// both to write the file and, by <see cref="LibraryConsistencyService"/>, to compute what the
    /// file's content should be for drift detection - the two must never diverge, or "correct"
    /// would mean two different things depending on which one you asked.
    /// </summary>
    public static string BuildOpfContent(Audiobook audiobook)
    {
        var metadata = new XElement(_opfNamespace + "metadata",
            new XAttribute(XNamespace.Xmlns + "dc", _dcNamespace),
            new XAttribute(XNamespace.Xmlns + "opf", _opfNamespace),
            new XElement(_dcNamespace + "title", audiobook.BookName ?? ""));

        foreach (var author in audiobook.Authors)
        {
            metadata.Add(new XElement(_dcNamespace + "creator",
                new XAttribute(_opfNamespace + "role", "aut"),
                author.Name));
        }

        foreach (var narrator in audiobook.Narrators)
        {
            metadata.Add(new XElement(_dcNamespace + "contributor",
                new XAttribute(_opfNamespace + "role", "nrt"),
                narrator.Name));
        }

        if (!string.IsNullOrEmpty(audiobook.Description))
        {
            metadata.Add(new XElement(_dcNamespace + "description", audiobook.Description));
        }

        if (!string.IsNullOrEmpty(audiobook.Publisher))
        {
            metadata.Add(new XElement(_dcNamespace + "publisher", audiobook.Publisher));
        }

        if (audiobook.Year is not null)
        {
            metadata.Add(new XElement(_dcNamespace + "date", audiobook.Year.ToString()));
        }

        if (!string.IsNullOrEmpty(audiobook.Language))
        {
            metadata.Add(new XElement(_dcNamespace + "language", audiobook.Language));
        }

        foreach (var genre in audiobook.Genres)
        {
            metadata.Add(new XElement(_dcNamespace + "subject", genre));
        }

        if (!string.IsNullOrEmpty(audiobook.Asin))
        {
            metadata.Add(new XElement(_dcNamespace + "identifier",
                new XAttribute(_opfNamespace + "scheme", "ASIN"),
                audiobook.Asin));
        }

        if (!string.IsNullOrEmpty(audiobook.Series))
        {
            metadata.Add(new XElement(_opfNamespace + "meta",
                new XAttribute("name", "calibre:series"),
                new XAttribute("content", audiobook.Series)));

            if (!string.IsNullOrEmpty(audiobook.SeriesPart))
            {
                metadata.Add(new XElement(_opfNamespace + "meta",
                    new XAttribute("name", "calibre:series_index"),
                    new XAttribute("content", audiobook.SeriesPart)));
            }
        }

        var package = new XElement(_opfNamespace + "package",
            new XAttribute("version", "2.0"),
            metadata);

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), package);

        using var writer = new StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    public string? WriteCover(Audiobook audiobook)
    {
        var directoryPath = Path.GetDirectoryName(audiobook.FileInfo.FullPath)!;
        if (audiobook.Cover is not null)
        {
            var coverExtension = GetMimeFileExt(audiobook.Cover.MimeType);
            var fileName = JoinPaths(directoryPath, $"cover{coverExtension}");
            _fileOperations.WriteAllBytes(fileName, Convert.FromBase64String(audiobook.Cover.Base64Data), "audiobook cover image");

            // Replacing a JPEG cover with a PNG one (or the reverse) used to leave both files in
            // the directory. Only one of them is the cover this book actually has, and which of
            // the two a reader picks up is its own business - so the one we did not just write
            // goes, rather than leaving a stale image behind to compete with the real one.
            foreach (var otherExtension in _coverExtensions.Where(e => e != coverExtension))
            {
                _fileOperations.DeleteFileIfExists(JoinPaths(directoryPath, $"cover{otherExtension}"), "conflicting cover format cleanup");
            }

            return fileName;
        }

        return GetExistingCoverPath(directoryPath, cleanupDuplicate: true);
    }

    public static string? WriteCoverStatic(Audiobook audiobook) =>
        new AudiobookFileHandler(_defaultFileOperations).WriteCover(audiobook);

    /// <summary>
    /// Finds the cover.jpg/cover.png sidecar already sitting in <paramref name="directoryPath"/>,
    /// if any. Shared by <see cref="WriteCover"/> (when the caller sends no new cover, the
    /// existing sidecar - if any - is what the book's CoverFilePath resolves to) and by
    /// FileService's discovered-audiobook cover lookup, which has no Audiobook object to write
    /// through WriteCover in the first place - an untracked file has no DB row yet.
    ///
    /// <paramref name="cleanupDuplicate"/> gates the "both a .jpg and a .png exist, delete the
    /// .png" tie-break: that is a real (if minor) file mutation, appropriate when WriteCover is
    /// already saving the book that owns this directory, but never appropriate for a passive
    /// lookup against a directory nothing has confirmed is even an audiobook's - the discovered
    /// list preview must stay read-only, so it always passes false.
    /// </summary>
    public string? GetExistingCoverPath(string directoryPath, bool cleanupDuplicate)
    {
        var jpgPath = JoinPaths(directoryPath, "cover.jpg");
        var pngPath = JoinPaths(directoryPath, "cover.png");
        var jpgExists = File.Exists(jpgPath);
        var pngExists = File.Exists(pngPath);

        if (jpgExists && pngExists)
        {
            if (cleanupDuplicate)
            {
                _fileOperations.DeleteFileIfExists(pngPath, "duplicate cover format cleanup (preferring jpg)");
            }
            return jpgPath;
        }

        if (jpgExists)
        {
            return jpgPath;
        }

        if (pngExists)
        {
            return pngPath;
        }

        return null;
    }

    public static string? GetExistingCoverPathStatic(string directoryPath, bool cleanupDuplicate) =>
        new AudiobookFileHandler(_defaultFileOperations).GetExistingCoverPath(directoryPath, cleanupDuplicate);

    /// <summary>
    /// Moves existing cover sidecar files from <paramref name="oldDirectory"/> to
    /// <paramref name="newDirectory"/> before <paramref name="oldDirectory"/> is cleaned up,
    /// so a book without embedded artwork in its m4b does not lose its cover image on relocation.
    /// </summary>
    public void MigrateSidecarFiles(string oldDirectory, string newDirectory)
    {
        if (Directory.Exists(oldDirectory) && Directory.Exists(newDirectory) && !PathsEqual(oldDirectory, newDirectory))
        {
            foreach (var coverExt in _coverExtensions)
            {
                var oldCover = JoinPaths(oldDirectory, $"cover{coverExt}");
                var newCover = JoinPaths(newDirectory, $"cover{coverExt}");
                if (File.Exists(oldCover) && !File.Exists(newCover))
                {
                    _fileOperations.MoveFile(oldCover, newCover, false, "migrating cover sidecar to new directory");
                }
            }
        }
    }

    public static void MigrateSidecarFilesStatic(string oldDirectory, string newDirectory) =>
        new AudiobookFileHandler(_defaultFileOperations).MigrateSidecarFiles(oldDirectory, newDirectory);

    public void RemoveDirIfEmpty(string directoryPath)
    {
        _fileOperations.DeleteDirectoryIfEmpty(directoryPath, "cleaning up empty directory");
    }

    public static void RemoveDirIfEmptyStatic(string directoryPath) =>
        new AudiobookFileHandler(_defaultFileOperations).RemoveDirIfEmpty(directoryPath);

    public void RemoveSidecarFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var fileName in _sidecarFileNames)
        {
            var filePath = JoinPaths(directoryPath, fileName);
            _fileOperations.DeleteFileIfExists(filePath, "cleaning up old directory sidecar");
        }
    }

    public static void RemoveSidecarFilesStatic(string directoryPath) =>
        new AudiobookFileHandler(_defaultFileOperations).RemoveSidecarFiles(directoryPath);

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

    public static string JoinPaths(string path1, string path2) => $"{path1.GetSafeCompletePath()}{AudiobookPathExtensions.GetDirectorySeparator()}{path2.GetSafeCompletePath()}";

    public static string CombinePathAndFilename(IEnumerable<string> pathParts, string fileName, string extension) =>
        GetSafeCombinedPath(pathParts.Concat(new[] { $"{fileName}{extension.GetExtensionWithDot()}" }));

    public static string GetSafeCombinedPath(IEnumerable<string> pathParts) =>
        pathParts.Aggregate(string.Empty, (acc, curr) => string.IsNullOrEmpty(acc) ? curr.GetSafeFileName() : acc + AudiobookPathExtensions.GetDirectorySeparator() + curr.GetSafeFileName());

    public static string GetSafeCompletePath(string path) => path.GetSafeCompletePath();

    public static string GetSafeFileName(string fileName) => fileName.GetSafeFileName();

    public static char GetDirectorySeparator() => AudiobookPathExtensions.GetDirectorySeparator();

    private void MakeMetadataFile(string directoryPath, string fileName, string content)
    {
        var filePath = JoinPaths(directoryPath, fileName);
        _fileOperations.WriteAllText(filePath, content, $"audiobook {fileName} sidecar");
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

public static class AudiobookPathExtensions
{
    private const string _replacementInvalidPathSeparator = "_";
    private const string _replaceInvalidPathOrFileNameCharacter = "";
    private const char _preferredDirectorySeparatorChar = '/';
    private static char[] _systemDirectorySeparators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static string GetSafeCompletePath(this string path)
        => path.ReplaceChars(Path.GetInvalidPathChars(), _replaceInvalidPathOrFileNameCharacter);

    public static string GetSafeFileName(this string fileName)
        => fileName.ReplaceCharsAndPathSeparators(Path.GetInvalidFileNameChars(), _replaceInvalidPathOrFileNameCharacter);

    public static char GetDirectorySeparator() => _systemDirectorySeparators.Contains(_preferredDirectorySeparatorChar) ? _preferredDirectorySeparatorChar : Path.DirectorySeparatorChar;

    public static string GetExtensionWithDot(this string extension) => extension.StartsWith('.') ? extension : $".{extension}";

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
}
