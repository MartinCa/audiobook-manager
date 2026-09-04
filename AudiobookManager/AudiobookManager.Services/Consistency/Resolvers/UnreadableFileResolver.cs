using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>
/// Re-reads a book whose media file could not be parsed.
///
/// There is nothing this application can do to a corrupt m4b, so "resolve" here means "look
/// again": the file may have finished copying, the share may have come back, the permissions may
/// have been fixed. Deliberately not modelled on <see cref="MissingMediaFileResolver"/>, which
/// deletes the library record when the file is really gone - an unreadable file is still a file,
/// and the record is the only place the curated metadata lives.
/// </summary>
public class UnreadableFileResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[] { ConsistencyIssueType.UnreadableFile };

    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookIssueDetectionService _detectionService;
    private readonly ILogger<UnreadableFileResolver> _logger;

    public UnreadableFileResolver(
        IConsistencyIssueRepository issueRepository,
        IAudiobookIssueDetectionService detectionService,
        ILogger<UnreadableFileResolver> logger)
    {
        _issueRepository = issueRepository;
        _detectionService = detectionService;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;

        // Detection is what decides whether the file reads now - re-running it is the whole
        // resolve, and it returns the book's real issues if it does.
        var newIssues = await Task.Run(() => _detectionService.DetectIssues(audiobook));

        var stillUnreadable = newIssues
            .Where(newIssue => newIssue.IssueType == ConsistencyIssueType.UnreadableFile)
            .ToList();

        if (stillUnreadable.Count > 0)
        {
            // Only this type is replaced. Detection short-circuits on an unreadable file, so it
            // returned nothing about the book's sidecars, tags or path - and deleting the stored
            // issues for those would discard findings that were never re-evaluated. Those are
            // about files *beside* the m4b and are still true whether or not the m4b parses.
            //
            // This is the shape TagOrPathMismatchResolver's comment records as a past bug: a
            // resolver that cleared every issue for a book on success, silently taking a
            // coexisting mismatch it had never touched with it.
            await _issueRepository.DeleteByAudiobookIdAndTypesAsync(
                audiobook.Id, new[] { ConsistencyIssueType.UnreadableFile });
            await _issueRepository.InsertRangeAsync(stillUnreadable);

            _logger.LogInformation(
                "Media file for audiobook {AudiobookId} ('{Title}') at '{FilePath}' still cannot be read.",
                audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

            // IssueOnly, not AllForAudiobook: nothing else about this book was touched, so a bulk
            // resolve must not treat the book's other issues as settled by this one.
            return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "still_unreadable",
                "The media file still cannot be read. It is most likely corrupt, incompletely "
                + "copied, or not readable by the user this application runs as."));
        }

        // Readable again, so detection did re-evaluate the whole book and its answer is complete.
        // Replacing every issue is correct here, and is what refreshes the book's status.
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        if (newIssues.Count > 0)
        {
            await _issueRepository.InsertRangeAsync(newIssues);
        }

        _logger.LogInformation(
            "Media file for audiobook {AudiobookId} ('{Title}') at '{FilePath}' can be read again; refreshed consistency status.",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "file_readable",
            "The media file can be read again. Refreshed consistency status for this book."));
    }
}
