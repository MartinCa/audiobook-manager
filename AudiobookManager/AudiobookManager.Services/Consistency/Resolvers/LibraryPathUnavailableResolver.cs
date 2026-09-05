using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>
/// Re-reads a book whose media file's directory could not be found.
///
/// A missing file *plus* a missing parent directory is the shape of an unmounted subtree (a dead
/// per-author or per-share mount), not of a deleted book, so "resolve" here means "look again" -
/// the share may have come back - exactly as <see cref="UnreadableFileResolver"/> does for a file
/// that cannot be read. The one thing this resolver must never do is what
/// <see cref="MissingMediaFileResolver"/> does: <c>MissingMediaFile</c> deletes the library
/// record, and that is correct only because a missing file whose directory is still there is a
/// genuine deletion. A book whose whole directory has vanished is still on disk somewhere - on a
/// mount that is not currently attached - and deleting the record would drop the only copy of its
/// curated metadata.
/// </summary>
public class LibraryPathUnavailableResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[] { ConsistencyIssueType.LibraryPathUnavailable };

    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookIssueDetectionService _detectionService;
    private readonly ILogger<LibraryPathUnavailableResolver> _logger;

    public LibraryPathUnavailableResolver(
        IConsistencyIssueRepository issueRepository,
        IAudiobookIssueDetectionService detectionService,
        ILogger<LibraryPathUnavailableResolver> logger)
    {
        _issueRepository = issueRepository;
        _detectionService = detectionService;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;

        // Detection is what decides whether the directory is back - re-running it is the whole
        // resolve, and it returns the book's real issues if it is.
        var newIssues = await Task.Run(() => _detectionService.DetectIssues(audiobook));

        var stillUnavailable = newIssues
            .Where(newIssue => newIssue.IssueType == ConsistencyIssueType.LibraryPathUnavailable)
            .ToList();

        if (stillUnavailable.Count > 0)
        {
            // Only this type is replaced, for the same reason UnreadableFileResolver replaces
            // only its own: detection short-circuits when the directory is missing, so it said
            // nothing about the book's sidecars, tags or path - and deleting the stored issues
            // for those would discard findings that were never re-evaluated.
            await _issueRepository.DeleteByAudiobookIdAndTypesAsync(
                audiobook.Id, new[] { ConsistencyIssueType.LibraryPathUnavailable });
            await _issueRepository.InsertRangeAsync(stillUnavailable);

            _logger.LogInformation(
                "Directory of media file for audiobook {AudiobookId} ('{Title}') at '{FilePath}' still is not available.",
                audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

            // IssueOnly, not AllForAudiobook: nothing else about this book was touched, so a bulk
            // resolve must not treat the book's other issues as settled by this one.
            return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "directory_still_unavailable",
                "The media file's directory still cannot be found. It is most likely an unmounted "
                + "directory on a drive or share that has gone away - check the mount and re-run."));
        }

        // The directory is back, so detection did re-evaluate the whole book and its answer is
        // complete. Replacing every issue is correct here, and is what refreshes the book's status.
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        if (newIssues.Count > 0)
        {
            await _issueRepository.InsertRangeAsync(newIssues);
        }

        _logger.LogInformation(
            "Directory of media file for audiobook {AudiobookId} ('{Title}') at '{FilePath}' is available again; refreshed consistency status.",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "directory_readable_again",
            "The media file's directory is available again. Refreshed consistency status for this book."));
    }
}