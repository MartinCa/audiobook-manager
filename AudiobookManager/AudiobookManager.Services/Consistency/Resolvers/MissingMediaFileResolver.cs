using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class MissingMediaFileResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[] { ConsistencyIssueType.MissingMediaFile };

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookFileHandler _fileHandler;
    private readonly IAudiobookIssueDetectionService _detectionService;
    private readonly ILogger<MissingMediaFileResolver> _logger;

    public MissingMediaFileResolver(
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        IAudiobookFileHandler fileHandler,
        IAudiobookIssueDetectionService detectionService,
        ILogger<MissingMediaFileResolver> logger)
    {
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _fileHandler = fileHandler;
        _detectionService = detectionService;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;

        if (File.Exists(audiobook.FileInfoFullPath))
        {
            _logger.LogInformation(
                "Media file for audiobook {AudiobookId} ('{Title}') found at '{FilePath}'. Preserving audiobook and re-evaluating consistency.",
                audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

            await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
            var newIssues = await Task.Run(() => _detectionService.DetectIssues(audiobook));
            if (newIssues.Count > 0)
            {
                await _issueRepository.InsertRangeAsync(newIssues);
            }

            return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "file_recovered",
                "Media file was found on disk. Preserved audiobook and refreshed consistency status."));
        }

        var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath);

        // This issue was detected (or stored) while the file was missing *with its parent
        // directory still there* - which is a genuine deletion. But the mount can have died
        // since: a book whose whole directory has vanished must never be answered with the
        // resolution that deletes the library record, exactly as #1311's detection change
        // classifies it. Re-check, which now reports the real state, instead of deleting on
        // evidence that may mean the opposite.
        if (directoryPath is null || !Directory.Exists(directoryPath))
        {
            _logger.LogInformation(
                "Media file for audiobook {AudiobookId} ('{Title}') at '{FilePath}' is missing and so is its directory; re-checking instead of deleting the record.",
                audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

            // Only this type is replaced. Detection short-circuits when the directory is missing,
            // so it said nothing about the book's sidecars, tags or path - and deleting the stored
            // issues for those would discard findings that were never re-evaluated. The re-check's
            // answer (LibraryPathUnavailable, the stock "share is gone" shape) is inserted below.
            await _issueRepository.DeleteByAudiobookIdAndTypesAsync(
                audiobook.Id, new[] { ConsistencyIssueType.MissingMediaFile });
            var newIssues = await Task.Run(() => _detectionService.DetectIssues(audiobook));
            if (newIssues.Count > 0)
            {
                await _issueRepository.InsertRangeAsync(newIssues);
            }

            // IssueOnly, not AllForAudiobook: nothing else about this book was touched, so a bulk
            // resolve must not treat the book's other issues as settled by this one.
            return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "directory_unavailable",
                "Media file not found and its directory is not available - most likely an unmounted "
                + "drive or share rather than a deleted book. Preserved the audiobook; if it really is "
                + "gone, delete it once the directory is back."));
        }

        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        await _audiobookRepository.DeleteAudiobookAsync(audiobook.Id);

        if (directoryPath != null)
        {
            _fileHandler.RemoveDirIfEmpty(directoryPath);
        }

        _logger.LogInformation(
            "Media file for audiobook {AudiobookId} ('{Title}') not found at '{FilePath}'. Deleted audiobook from database and cleaned up empty directory.",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "audiobook_deleted",
            "Media file not found. Audiobook record deleted from library."));
    }
}
