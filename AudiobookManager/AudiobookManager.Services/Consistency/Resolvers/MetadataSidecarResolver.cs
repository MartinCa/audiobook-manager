using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class MetadataSidecarResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[]
    {
        ConsistencyIssueType.MissingDescTxt, ConsistencyIssueType.IncorrectDescTxt,
        ConsistencyIssueType.MissingReaderTxt, ConsistencyIssueType.IncorrectReaderTxt,
        ConsistencyIssueType.MissingOpfFile, ConsistencyIssueType.IncorrectOpfFile
    };

    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookFileHandler _fileHandler;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly ILogger<MetadataSidecarResolver> _logger;

    public MetadataSidecarResolver(
        IAudiobookTagHandler tagHandler,
        IAudiobookFileHandler fileHandler,
        IConsistencyIssueRepository issueRepository,
        ILogger<MetadataSidecarResolver> logger)
    {
        _tagHandler = tagHandler;
        _fileHandler = fileHandler;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        _fileHandler.WriteMetadata(parsed);

        _logger.LogInformation(
            "Rewrote metadata sidecars (desc.txt, reader.txt, metadata.opf) for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        // WriteMetadata writes all three, so every desc/reader/opf issue for this book is settled.
        await _issueRepository.DeleteByAudiobookIdAndTypesAsync(audiobook.Id, HandledTypes);

        return (ResolveScope.SidecarsForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Metadata sidecar files updated."));
    }
}
