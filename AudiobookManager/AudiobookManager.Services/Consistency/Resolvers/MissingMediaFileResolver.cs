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

        _logger.LogInformation(
            "Media file for audiobook {AudiobookId} ('{Title}') not found at '{FilePath}'. Deleted audiobook from database and cleaned up empty directory.",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath);

        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        await _audiobookRepository.DeleteAudiobookAsync(audiobook.Id);

        if (directoryPath != null)
        {
            _fileHandler.RemoveDirIfEmpty(directoryPath);
        }

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "audiobook_deleted",
            "Media file not found. Audiobook record deleted from library."));
    }
}
