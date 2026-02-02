using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class LibraryConsistencyService : ILibraryConsistencyService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly ILogger<LibraryConsistencyService> _logger;

    public LibraryConsistencyService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        IAudiobookTagHandler tagHandler,
        ILogger<LibraryConsistencyService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _tagHandler = tagHandler;
        _logger = logger;
    }

    public async Task RunConsistencyCheck(Func<string, int, int, int, Task> progressAction)
    {
        _logger.LogInformation("Starting library consistency check");

        await _issueRepository.ClearAllAsync();

        var audiobooks = await _audiobookRepository.GetAllWithIncludesAsync();
        var totalBooks = audiobooks.Count;
        var booksChecked = 0;
        var issuesFound = 0;

        foreach (var audiobook in audiobooks)
        {
            booksChecked++;
            var bookLabel = $"{string.Join(", ", audiobook.Authors.Select(a => a.Name))} — {audiobook.BookName}";

            if (!File.Exists(audiobook.FileInfoFullPath))
            {
                await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingMediaFile,
                    $"Media file not found: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, null);
                issuesFound++;
                await progressAction($"Missing: {bookLabel}", booksChecked, totalBooks, issuesFound);
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
                var parsed = _tagHandler.ParseAudiobook(fileInfo);

                // Check file path
                var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsed);
                var expectedFullPath = AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, expectedRelativePath);
                if (!string.Equals(audiobook.FileInfoFullPath, expectedFullPath, StringComparison.Ordinal))
                {
                    await InsertIssue(audiobook.Id, ConsistencyIssueType.WrongFilePath,
                        "File path does not match expected path from tags",
                        expectedFullPath, audiobook.FileInfoFullPath);
                    issuesFound++;
                }

                var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath)!;

                // Check desc.txt
                var descPath = AudiobookFileHandler.JoinPaths(directoryPath, "desc.txt");
                if (!string.IsNullOrEmpty(parsed.Description))
                {
                    if (!File.Exists(descPath))
                    {
                        await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingDescTxt,
                            "desc.txt missing but m4b has Description tag",
                            parsed.Description, null);
                        issuesFound++;
                    }
                    else
                    {
                        var descContent = await File.ReadAllTextAsync(descPath);
                        if (!string.Equals(descContent, parsed.Description, StringComparison.Ordinal))
                        {
                            await InsertIssue(audiobook.Id, ConsistencyIssueType.IncorrectDescTxt,
                                "desc.txt content does not match Description tag",
                                parsed.Description, descContent);
                            issuesFound++;
                        }
                    }
                }

                // Check reader.txt
                var readerPath = AudiobookFileHandler.JoinPaths(directoryPath, "reader.txt");
                if (parsed.Narrators.Any())
                {
                    var expectedNarrators = string.Join(", ", parsed.Narrators.Select(n => n.Name));
                    if (!File.Exists(readerPath))
                    {
                        await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingReaderTxt,
                            "reader.txt missing but m4b has Narrators tag",
                            expectedNarrators, null);
                        issuesFound++;
                    }
                    else
                    {
                        var readerContent = await File.ReadAllTextAsync(readerPath);
                        if (!string.Equals(readerContent, expectedNarrators, StringComparison.Ordinal))
                        {
                            await InsertIssue(audiobook.Id, ConsistencyIssueType.IncorrectReaderTxt,
                                "reader.txt content does not match Narrators tag",
                                expectedNarrators, readerContent);
                            issuesFound++;
                        }
                    }
                }

                // Check cover file
                if (parsed.Cover is not null)
                {
                    var coverExists = File.Exists(AudiobookFileHandler.JoinPaths(directoryPath, "cover.jpg"))
                        || File.Exists(AudiobookFileHandler.JoinPaths(directoryPath, "cover.png"));
                    if (!coverExists)
                    {
                        await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                            "Cover file missing but m4b has embedded cover",
                            "cover.jpg or cover.png", null);
                        issuesFound++;
                    }
                }

                await progressAction($"Checked: {bookLabel}", booksChecked, totalBooks, issuesFound);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check consistency for {FilePath}", audiobook.FileInfoFullPath);
                await progressAction($"Error checking: {bookLabel}", booksChecked, totalBooks, issuesFound);
            }
        }

        _logger.LogInformation("Consistency check complete. Books: {Total}, Issues: {Issues}", totalBooks, issuesFound);
    }

    public async Task ResolveIssue(long issueId)
    {
        var issue = await _issueRepository.GetByIdAsync(issueId);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found");

        switch (issue.IssueType)
        {
            case ConsistencyIssueType.MissingMediaFile:
                await ResolveMissingMediaFile(issue);
                break;

            case ConsistencyIssueType.WrongFilePath:
                await ResolveWrongFilePath(issue);
                break;

            case ConsistencyIssueType.MissingDescTxt:
            case ConsistencyIssueType.IncorrectDescTxt:
            case ConsistencyIssueType.MissingReaderTxt:
            case ConsistencyIssueType.IncorrectReaderTxt:
                await ResolveMetadataIssue(issue);
                break;

            case ConsistencyIssueType.MissingCoverFile:
                await ResolveMissingCover(issue);
                break;
        }
    }

    private async Task ResolveMissingMediaFile(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath);

        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        await _audiobookRepository.DeleteAudiobookAsync(audiobook.Id);

        if (directoryPath != null)
        {
            AudiobookFileHandler.RemoveDirIfEmpty(directoryPath);
        }
    }

    private async Task ResolveWrongFilePath(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsed);
        var expectedFullPath = AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, expectedRelativePath);

        var oldDirectory = Path.GetDirectoryName(audiobook.FileInfoFullPath);

        AudiobookFileHandler.RelocateAudiobook(parsed, expectedFullPath);

        // Re-parse from new location
        var newFileInfo = new FileInfo(expectedFullPath);
        var newParsed = _tagHandler.ParseAudiobook(newFileInfo);

        AudiobookFileHandler.WriteMetadata(newParsed);
        var coverPath = AudiobookFileHandler.WriteCover(newParsed);

        var newFileName = Path.GetFileName(expectedFullPath);
        await _audiobookRepository.UpdateFilePathAsync(audiobook.Id, expectedFullPath, newFileName);
        await _audiobookRepository.UpdateCoverFilePathAsync(audiobook.Id, coverPath);

        if (oldDirectory != null)
        {
            AudiobookFileHandler.RemoveDirIfEmpty(oldDirectory);
        }

        // Path change invalidates all other checks for this book
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
    }

    private async Task ResolveMetadataIssue(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        AudiobookFileHandler.WriteMetadata(parsed);

        // Delete all desc/reader issues for this book since WriteMetadata writes both
        await _issueRepository.DeleteByAudiobookIdAndTypesAsync(audiobook.Id, new[]
        {
            ConsistencyIssueType.MissingDescTxt, ConsistencyIssueType.IncorrectDescTxt,
            ConsistencyIssueType.MissingReaderTxt, ConsistencyIssueType.IncorrectReaderTxt
        });
    }

    private async Task ResolveMissingCover(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var coverPath = AudiobookFileHandler.WriteCover(parsed);
        await _audiobookRepository.UpdateCoverFilePathAsync(audiobook.Id, coverPath);

        await _issueRepository.DeleteAsync(issue.Id);
    }

    public async Task<(int resolved, int failed)> ResolveIssuesByType(string issueType)
    {
        if (!Enum.TryParse<ConsistencyIssueType>(issueType, out var parsedType))
            throw new ArgumentException($"Unknown issue type: {issueType}");

        var issues = await _issueRepository.GetByTypeAsync(parsedType);
        var resolved = 0;
        var failed = 0;

        foreach (var issue in issues)
        {
            try
            {
                await ResolveIssue(issue.Id);
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve issue {IssueId} during bulk resolve", issue.Id);
                failed++;
            }
        }

        return (resolved, failed);
    }

    private async Task InsertIssue(long audiobookId, ConsistencyIssueType issueType, string description, string? expectedValue, string? actualValue)
    {
        var issue = new ConsistencyIssue
        {
            AudiobookId = audiobookId,
            IssueType = issueType,
            Description = description,
            ExpectedValue = expectedValue,
            ActualValue = actualValue,
            DetectedAt = DateTime.UtcNow
        };
        await _issueRepository.InsertAsync(issue);
    }
}
