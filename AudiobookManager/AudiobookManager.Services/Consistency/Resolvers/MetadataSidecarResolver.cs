using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class MetadataSidecarResolver : IBookConsistencyIssueResolver
{
    public IReadOnlyCollection<BookConsistencyIssueType> HandledTypes { get; } = new[]
    {
        BookConsistencyIssueType.MissingDescTxt, BookConsistencyIssueType.IncorrectDescTxt,
        BookConsistencyIssueType.MissingReaderTxt, BookConsistencyIssueType.IncorrectReaderTxt,
        BookConsistencyIssueType.MissingOpfFile, BookConsistencyIssueType.IncorrectOpfFile
    };

    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookFileHandler _fileHandler;
    private readonly IBookConsistencyIssueRepository _issueRepository;
    private readonly ILogger<MetadataSidecarResolver> _logger;

    public MetadataSidecarResolver(
        IAudiobookTagHandler tagHandler,
        IAudiobookFileHandler fileHandler,
        IBookConsistencyIssueRepository issueRepository,
        ILogger<MetadataSidecarResolver> logger)
    {
        _tagHandler = tagHandler;
        _fileHandler = fileHandler;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, BookConsistencyResolveResult Result)> ResolveAsync(BookConsistencyIssue issue)
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

        return (ResolveScope.SidecarsForAudiobook, new BookConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Metadata sidecar files updated."));
    }
}
