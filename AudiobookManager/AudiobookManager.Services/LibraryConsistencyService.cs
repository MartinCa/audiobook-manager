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
    private readonly IOrphanDirectoryRepository _orphanDirectoryRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookService _audiobookService;
    private readonly ILogger<LibraryConsistencyService> _logger;

    public LibraryConsistencyService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        IOrphanDirectoryRepository orphanDirectoryRepository,
        IAudiobookTagHandler tagHandler,
        IAudiobookService audiobookService,
        ILogger<LibraryConsistencyService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _orphanDirectoryRepository = orphanDirectoryRepository;
        _tagHandler = tagHandler;
        _audiobookService = audiobookService;
        _logger = logger;
    }

    public async Task<(int BooksChecked, int IssuesFound)> RunConsistencyCheck(Func<string, int, int, int, Task> progressAction)
    {
        _logger.LogInformation("Starting library consistency check");

        await _issueRepository.ClearAllAsync();
        await _orphanDirectoryRepository.ClearAllAsync();

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

                // Check tag values against library metadata
                var tagMismatches = FindTagMismatches(audiobook, parsed);
                if (tagMismatches.Count > 0)
                {
                    var description = $"m4b tags do not match library metadata: {string.Join(", ", tagMismatches.Select(m => m.Field))}";
                    var expectedValue = string.Join("\n", tagMismatches.Select(m => $"{m.Field}: {m.Expected}"));
                    var actualValue = string.Join("\n", tagMismatches.Select(m => $"{m.Field}: {m.Actual}"));
                    await InsertIssue(audiobook.Id, ConsistencyIssueType.TagMismatch, description, expectedValue, actualValue);
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

        issuesFound = await CheckForOrphanDirectories(progressAction, totalBooks, issuesFound);

        _logger.LogInformation("Consistency check complete. Books: {Total}, Issues: {Issues}", totalBooks, issuesFound);

        return (totalBooks, issuesFound);
    }

    private async Task<int> CheckForOrphanDirectories(Func<string, int, int, int, Task> progressAction, int totalBooks, int issuesFound)
    {
        if (!Directory.Exists(_settings.AudiobookLibraryPath))
        {
            return issuesFound;
        }

        var leafDirectories = Directory.GetDirectories(_settings.AudiobookLibraryPath, "*", SearchOption.AllDirectories)
            .Where(directory => !Directory.GetDirectories(directory).Any());

        foreach (var directory in leafDirectories)
        {
            var hasAudioFile = Directory.GetFiles(directory).Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)));
            if (!hasAudioFile)
            {
                await _orphanDirectoryRepository.InsertAsync(new OrphanDirectory
                {
                    DirectoryPath = directory,
                    DetectedAt = DateTime.UtcNow
                });
                issuesFound++;
            }
        }

        await progressAction("Checked library directories for orphaned folders", totalBooks, totalBooks, issuesFound);

        return issuesFound;
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

            case ConsistencyIssueType.TagMismatch:
                await ResolveTagMismatch(issue);
                break;
        }
    }

    private async Task ResolveMissingMediaFile(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;

        if (File.Exists(audiobook.FileInfoFullPath))
        {
            // The file has reappeared since the issue was detected; the audiobook is no longer missing.
            await _issueRepository.DeleteAsync(issue.Id);
            return;
        }

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

        if (!File.Exists(audiobook.FileInfoFullPath))
        {
            throw new FileNotFoundException(
                $"Media file not found, cannot resolve wrong file path: {audiobook.FileInfoFullPath}",
                audiobook.FileInfoFullPath);
        }

        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsed);
        var expectedFullPath = AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, expectedRelativePath);

        if (string.Equals(audiobook.FileInfoFullPath, expectedFullPath, StringComparison.Ordinal))
        {
            // The path already matches; the issue is no longer valid.
            await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
            return;
        }

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

        if (oldDirectory != null && oldDirectory != Path.GetDirectoryName(expectedFullPath))
        {
            AudiobookFileHandler.RemoveSidecarFiles(oldDirectory);
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

    private async Task ResolveTagMismatch(ConsistencyIssue issue)
    {
        var dbAudiobook = await _audiobookRepository.GetByIdWithIncludesAsync(issue.AudiobookId);
        if (dbAudiobook == null)
            throw new KeyNotFoundException($"Audiobook {issue.AudiobookId} not found");

        // Rewrite the m4b tags (and relocate/resync sidecars if the path changes) from the
        // library metadata that's already correct in the database - see the "Binding invariant"
        // in CLAUDE.md, UpdateAudiobook is the only place that's allowed to touch these fields.
        var domain = AudiobookService.FromDb(dbAudiobook);
        await _audiobookService.UpdateAudiobook(issue.AudiobookId, domain);

        // Tags (and potentially the file path) changed, invalidating all other checks for this book
        await _issueRepository.DeleteByAudiobookIdAsync(issue.AudiobookId);
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

    public async Task ResolveOrphanDirectory(long orphanDirectoryId)
    {
        var directory = await _orphanDirectoryRepository.GetByIdAsync(orphanDirectoryId);
        if (directory == null)
            throw new KeyNotFoundException($"Orphan directory {orphanDirectoryId} not found");

        DeleteOrphanDirectoryFromDisk(directory.DirectoryPath);
        await _orphanDirectoryRepository.DeleteAsync(orphanDirectoryId);
    }

    public async Task<(int resolved, int failed)> ResolveAllOrphanDirectories()
    {
        var directories = await _orphanDirectoryRepository.GetAllAsync();
        var resolved = 0;
        var failed = 0;

        foreach (var directory in directories)
        {
            try
            {
                await ResolveOrphanDirectory(directory.Id);
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve orphan directory {DirectoryId}", directory.Id);
                failed++;
            }
        }

        return (resolved, failed);
    }

    private static void DeleteOrphanDirectoryFromDisk(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        // Safety net in case a file was added to the directory since it was detected as orphaned
        var hasAudioFile = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)));
        if (hasAudioFile)
        {
            return;
        }

        Directory.Delete(directoryPath, recursive: true);
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

    private static List<(string Field, string Expected, string Actual)> FindTagMismatches(Audiobook audiobook, AudiobookManager.Domain.Audiobook parsed)
    {
        var mismatches = new List<(string Field, string Expected, string Actual)>();

        void Compare(string field, string? expected, string? actual)
        {
            if (!string.Equals(expected ?? "", actual ?? "", StringComparison.Ordinal))
            {
                mismatches.Add((field, expected ?? "", actual ?? ""));
            }
        }

        Compare("Author", FormatPersons(audiobook.Authors), FormatPersons(parsed.Authors));
        Compare("Narrators", FormatPersons(audiobook.Narrators), FormatPersons(parsed.Narrators));
        Compare("Book Name", audiobook.BookName, parsed.BookName);
        Compare("Subtitle", audiobook.Subtitle, parsed.Subtitle);
        Compare("Series", audiobook.Series, parsed.Series);
        Compare("Series Part", audiobook.SeriesPart, parsed.SeriesPart);
        Compare("Year", audiobook.Year.ToString(), parsed.Year?.ToString());
        Compare("Description", audiobook.Description, parsed.Description);
        Compare("Copyright", audiobook.Copyright, parsed.Copyright);
        Compare("Publisher", audiobook.Publisher, parsed.Publisher);
        Compare("Rating", audiobook.Rating, parsed.Rating);
        Compare("Asin", audiobook.Asin, parsed.Asin);
        Compare("Www", audiobook.Www, parsed.Www);
        Compare("Genres", FormatGenres(audiobook.Genres.Select(g => g.Name)), FormatGenres(parsed.Genres));

        return mismatches;
    }

    private static string FormatGenres(IEnumerable<string> genres) =>
        string.Join(", ", genres.OrderBy(g => g, StringComparer.Ordinal));

    private static string FormatPersons(IEnumerable<Person> persons) =>
        string.Join(", ", persons.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

    private static string FormatPersons(IEnumerable<AudiobookManager.Domain.Person> persons) =>
        string.Join(", ", persons.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

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
