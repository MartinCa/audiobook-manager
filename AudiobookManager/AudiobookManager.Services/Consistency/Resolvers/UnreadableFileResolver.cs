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

        // The full set is replaced either way, so the re-inserted UnreadableFile below keeps the
        // book on the consistency screen rather than quietly vanishing from it.
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        if (newIssues.Count > 0)
        {
            await _issueRepository.InsertRangeAsync(newIssues);
        }

        if (newIssues.Any(newIssue => newIssue.IssueType == ConsistencyIssueType.UnreadableFile))
        {
            _logger.LogInformation(
                "Media file for audiobook {AudiobookId} ('{Title}') at '{FilePath}' still cannot be read.",
                audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

            return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "still_unreadable",
                "The media file still cannot be read. It is most likely corrupt, incompletely "
                + "copied, or not readable by the user this application runs as."));
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
