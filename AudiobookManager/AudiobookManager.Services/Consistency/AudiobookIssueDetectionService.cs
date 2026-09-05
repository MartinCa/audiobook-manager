using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class AudiobookIssueDetectionService : IAudiobookIssueDetectionService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IReadOnlyList<IConsistencyIssueDetector> _detectors;
    private readonly ILogger<AudiobookIssueDetectionService> _logger;

    public AudiobookIssueDetectionService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookTagHandler tagHandler,
        IEnumerable<IConsistencyIssueDetector> detectors,
        ILogger<AudiobookIssueDetectionService> logger)
    {
        _settings = settings.Value;
        _tagHandler = tagHandler;
        _detectors = detectors.ToList();
        _logger = logger;
    }

    private enum MediaFileState
    {
        /// <summary>The path names a file this process can stat. Whether it *parses* is a later question.</summary>
        Readable,

        /// <summary>
        /// Nothing is at the file's path, and the file's *parent directory* is still there - the
        /// shape a genuinely deleted book leaves behind. Answered with <see cref="ConsistencyIssueType.MissingMediaFile"/>,
        /// whose resolution deletes the library record.
        /// </summary>
        Missing,

        /// <summary>Something is there, but this process cannot get at it.</summary>
        Unreadable,

        /// <summary>
        /// Nothing is at the file's path and the parent directory is gone too - an unmounted
        /// subtree rather than a deleted book. Answered with <see cref="ConsistencyIssueType.LibraryPathUnavailable"/>,
        /// which is never resolved by deleting the record.
        /// </summary>
        DirectoryMissing,
    }

    /// <summary>
    /// Distinguishes "the file is gone" from "the file is there and unreachable", which
    /// <c>File.Exists</c> cannot: it is documented to return false "if the caller does not have
    /// sufficient permissions to read the specified file... regardless of the existence of path",
    /// and to swallow every error it hits. That collapse matters here more than almost anywhere
    /// else in the application, because the two answers have opposite resolutions - MissingMediaFile
    /// is resolved by *deleting the library record*, which is the only place a book's curated
    /// metadata lives. A library on a share whose permissions changed would otherwise offer to
    /// delete itself, one book at a time, for files that are all still present.
    ///
    /// GetAttributes is the cheapest call that reports the difference: it stats the path and
    /// throws rather than swallowing, including when a *parent* directory denies traversal.
    ///
    /// It also splits "nothing at the path" into its two real shapes by checking the parent
    /// directory. A genuinely deleted book leaves its directory behind (the app's own delete path
    /// removes it too, but an external or partial deletion typically doesn't), while an unmounted
    /// subtree - a dead per-author or per-share mount - takes the whole directory with it. The two
    /// are answered differently because one is a deletion and the other is a book that is still
    /// there, on a share that may come back: the second must never be resolved by deleting the
    /// record.
    /// </summary>
    private static (MediaFileState State, string? Detail) ProbeMediaFile(string fullPath)
    {
        try
        {
            var attributes = File.GetAttributes(fullPath);

            // A directory where the media file should be is not a missing file, and must not be
            // answered with the resolution that deletes the record.
            return attributes.HasFlag(FileAttributes.Directory)
                ? (MediaFileState.Unreadable, "A directory exists at the media file's path.")
                : (MediaFileState.Readable, null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            var parentPath = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(parentPath))
            {
                return (MediaFileState.DirectoryMissing, null);
            }

            return (MediaFileState.Missing, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return (MediaFileState.Unreadable, ex.Message);
        }
    }

    public List<ConsistencyIssue> DetectIssues(Audiobook audiobook)
    {
        var (state, detail) = ProbeMediaFile(audiobook.FileInfoFullPath);

        if (state == MediaFileState.Missing)
        {
            return new List<ConsistencyIssue>
            {
                ConsistencyIssueFactory.Create(audiobook.Id, ConsistencyIssueType.MissingMediaFile,
                    $"Media file not found: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, null)
            };
        }

        if (state == MediaFileState.DirectoryMissing)
        {
            // The file is missing and so is its parent directory. That is the shape of an
            // unmounted subtree (a dead per-author or per-share mount), not of a deleted book,
            // and it is reported separately so it can never be answered with the resolution that
            // deletes the library record - the share may come back.
            return new List<ConsistencyIssue>
            {
                ConsistencyIssueFactory.Create(audiobook.Id, ConsistencyIssueType.LibraryPathUnavailable,
                    $"Media file's directory is not available: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, null)
            };
        }

        if (state == MediaFileState.Unreadable)
        {
            _logger.LogWarning(
                "Media file for audiobook {AudiobookId} at '{FilePath}' exists but cannot be read: {Detail}",
                audiobook.Id, audiobook.FileInfoFullPath, detail);

            return new List<ConsistencyIssue>
            {
                ConsistencyIssueFactory.Create(audiobook.Id, ConsistencyIssueType.UnreadableFile,
                    $"Media file could not be read: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, detail)
            };
        }

        try
        {
            var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
            // Only asks whether a cover exists (parsed.Cover is not null), never for its bytes -
            // encoding them for every book in the library is wasted work.
            var parsed = _tagHandler.ParseAudiobook(fileInfo, includeCoverData: false);
            var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath)!;
            var context = new AudiobookCheckContext(audiobook, parsed, directoryPath, _settings.AudiobookLibraryPath);

            return _detectors.SelectMany(detector => detector.Detect(context)).ToList();
        }
        catch (Exception ex)
        {
            // An empty list is indistinguishable from a clean book, which is how a corrupt m4b, a
            // file ATL cannot parse, or a permission-denied directory used to render on the
            // consistency screen: perfectly consistent. The only signal was a warning in the
            // container log, where nobody is looking while reading that screen.
            _logger.LogWarning(ex, "Failed to check consistency for {FilePath}", audiobook.FileInfoFullPath);

            return new List<ConsistencyIssue>
            {
                ConsistencyIssueFactory.Create(audiobook.Id, ConsistencyIssueType.UnreadableFile,
                    $"Media file could not be read: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, ex.Message)
            };
        }
    }
}
