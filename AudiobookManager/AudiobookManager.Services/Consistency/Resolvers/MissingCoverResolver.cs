using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class MissingCoverResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[] { ConsistencyIssueType.MissingCoverFile };

    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookFileHandler _fileHandler;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly ILogger<MissingCoverResolver> _logger;

    public MissingCoverResolver(
        IAudiobookTagHandler tagHandler,
        IAudiobookFileHandler fileHandler,
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        ILogger<MissingCoverResolver> logger)
    {
        _tagHandler = tagHandler;
        _fileHandler = fileHandler;
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var coverPath = _fileHandler.WriteCover(parsed);
        await _audiobookRepository.UpdateCoverFilePathAsync(audiobook.Id, coverPath);

        _logger.LogInformation(
            "Extracted and wrote cover image for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        await _issueRepository.DeleteAsync(issue.Id);

        return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Cover file created."));
    }
}
