using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class LibraryConsistencyService : ILibraryConsistencyService
{
    /// <summary>Issues accumulated before a single batched insert.</summary>
    private const int InsertBatchSize = 500;

    /// <summary>Books checked between SignalR progress broadcasts during a full check.</summary>
    private const int ProgressBroadcastInterval = 25;

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

        // The issue table was just cleared, so this path needs neither the per-book delete nor a
        // SaveChanges per book: detection is pure, and the findings are inserted in batches. It
        // also broadcasts progress every ProgressBroadcastInterval books rather than every book -
        // the client throttles the bar to ~4fps anyway, so one message per book is thousands of
        // hub sends nobody can see.
        var pending = new List<ConsistencyIssue>(InsertBatchSize);

        foreach (var audiobook in audiobooks)
        {
            booksChecked++;
            var bookLabel = $"{string.Join(", ", audiobook.Authors.Select(a => a.Name))} — {audiobook.BookName}";

            var issues = DetectIssuesForAudiobook(audiobook);
            issuesFound += issues.Count;
            pending.AddRange(issues);

            if (pending.Count >= InsertBatchSize)
            {
                await _issueRepository.InsertRangeAsync(pending);
                pending.Clear();
            }

            if (booksChecked % ProgressBroadcastInterval == 0 || booksChecked == totalBooks)
            {
                var message = issues.Any(i => i.IssueType == ConsistencyIssueType.MissingMediaFile)
                    ? $"Missing: {bookLabel}"
                    : $"Checked: {bookLabel}";
                await progressAction(message, booksChecked, totalBooks, issuesFound);
            }
        }

        if (pending.Count > 0)
        {
            await _issueRepository.InsertRangeAsync(pending);
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

        // A single recursive enumeration, plus a set of every directory that has at least one
        // subdirectory (its parent), lets us identify leaf directories without re-stat'ing the
        // tree for each one via a second Directory.GetDirectories(dir) call per directory.
        var allDirectories = Directory.EnumerateDirectories(_settings.AudiobookLibraryPath, "*", SearchOption.AllDirectories).ToList();
        var parentDirectories = new HashSet<string>(
            allDirectories.Select(Path.GetDirectoryName).OfType<string>(),
            AudiobookFileHandler.PathComparer);
        var leafDirectories = allDirectories.Where(directory => !parentDirectories.Contains(directory));

        var orphans = new List<OrphanDirectory>();
        foreach (var directory in leafDirectories)
        {
            var hasAudioFile = Directory.GetFiles(directory).Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)));
            if (!hasAudioFile)
            {
                orphans.Add(new OrphanDirectory
                {
                    DirectoryPath = directory,
                    DetectedAt = DateTime.UtcNow
                });
                issuesFound++;
            }
        }

        // One insert for the whole sweep rather than a SaveChanges per orphaned folder.
        await _orphanDirectoryRepository.InsertRangeAsync(orphans);

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

        if (AudiobookFileHandler.PathsEqual(audiobook.FileInfoFullPath, expectedFullPath))
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

        // OS-aware for consistency with the path check above; see the equivalent note in
        // AudiobookService.RelocateIfPathChangedAsync for why this is defensive rather than a
        // live bug fix.
        var newDirectory = Path.GetDirectoryName(expectedFullPath);
        if (oldDirectory != null && newDirectory != null && !AudiobookFileHandler.PathsEqual(oldDirectory, newDirectory))
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

        var (_, succeeded, failed) = await BulkOperationRunner.RunAsync(
            directories,
            directory => ResolveOrphanDirectory(directory.Id),
            _logger,
            directory => $"Failed to resolve orphan directory {directory.Id}");

        return (succeeded, failed);
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

        var (_, succeeded, failed) = await BulkOperationRunner.RunAsync(
            issues,
            issue => ResolveIssue(issue.Id),
            _logger,
            issue => $"Failed to resolve issue {issue.Id} during bulk resolve");

        return (succeeded, failed);
    }

    public async Task<(int resolved, int failed)> ResolveIssues(IEnumerable<long> issueIds)
    {
        var (_, succeeded, failed) = await BulkOperationRunner.RunAsync(
            issueIds.ToList(),
            ResolveIssue,
            _logger,
            issueId => $"Failed to resolve issue {issueId} during selected bulk resolve");

        return (succeeded, failed);
    }

    private static List<(string Field, string Expected, string Actual)> FindTagMismatches(Audiobook audiobook, AudiobookManager.Domain.Audiobook parsed)
    {
        return TagConsistencyChecker.FindMismatches(AudiobookService.FromDb(audiobook), parsed);
    }

    private static ConsistencyIssue BuildIssue(long audiobookId, ConsistencyIssueType issueType, string description, string? expectedValue, string? actualValue)
    {
        return new ConsistencyIssue
        {
            AudiobookId = audiobookId,
            IssueType = issueType,
            Description = description,
            ExpectedValue = expectedValue,
            ActualValue = actualValue,
            DetectedAt = DateTime.UtcNow
        };
    }

    public async Task<List<ConsistencyIssue>> RecheckAudiobookAsync(long audiobookId)
    {
        var audiobook = await _audiobookRepository.GetByIdWithIncludesAsync(audiobookId);
        if (audiobook == null)
            throw new KeyNotFoundException($"Audiobook {audiobookId} not found");

        return await DetectIssuesForAudiobookAsync(audiobook);
    }

    /// <summary>
    /// Replaces the stored issues for a single audiobook. Used by the single-book recheck, where
    /// the rest of the table must be left alone; the full check uses <see cref="DetectIssuesForAudiobook"/>
    /// with batched inserts instead, since it already cleared the table up front.
    /// </summary>
    private async Task<List<ConsistencyIssue>> DetectIssuesForAudiobookAsync(Audiobook audiobook)
    {
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);

        // Detection is blocking work - an ATL parse of the whole m4b, plus several file reads -
        // and this path is awaited directly by a controller action, so it must not run on the
        // request thread. (The full check needs no such wrapper: BackgroundOperationRunner
        // already puts it on the thread pool.)
        var issues = await Task.Run(() => DetectIssuesForAudiobook(audiobook));

        await _issueRepository.InsertRangeAsync(issues);
        return issues;
    }

    /// <summary>
    /// Pure detection: inspects the file on disk against the library metadata and returns the
    /// issues found, without touching the database.
    /// </summary>
    private List<ConsistencyIssue> DetectIssuesForAudiobook(Audiobook audiobook)
    {
        var issues = new List<ConsistencyIssue>();

        if (!File.Exists(audiobook.FileInfoFullPath))
        {
            issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingMediaFile,
                $"Media file not found: {audiobook.FileInfoFileName}",
                audiobook.FileInfoFullPath, null));
            return issues;
        }

        try
        {
            var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
            // Detection only asks whether a cover exists (parsed.Cover is not null), never for
            // its bytes - encoding them for every book in the library is wasted work.
            var parsed = _tagHandler.ParseAudiobook(fileInfo, includeCoverData: false);

            // Check file path
            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsed);
            var expectedFullPath = AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, expectedRelativePath);
            if (!AudiobookFileHandler.PathsEqual(audiobook.FileInfoFullPath, expectedFullPath))
            {
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.WrongFilePath,
                    "File path does not match expected path from tags",
                    expectedFullPath, audiobook.FileInfoFullPath));
            }

            // Check tag values against library metadata
            var tagMismatches = FindTagMismatches(audiobook, parsed);
            if (tagMismatches.Count > 0)
            {
                var description = $"m4b tags do not match library metadata: {string.Join(", ", tagMismatches.Select(m => m.Field))}";
                var expectedValue = string.Join("\n", tagMismatches.Select(m => $"{m.Field}: {m.Expected}"));
                var actualValue = string.Join("\n", tagMismatches.Select(m => $"{m.Field}: {m.Actual}"));
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.TagMismatch, description, expectedValue, actualValue));
            }

            var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath)!;

            // Check desc.txt
            var descPath = AudiobookFileHandler.JoinPaths(directoryPath, "desc.txt");
            if (!string.IsNullOrEmpty(parsed.Description))
            {
                if (!File.Exists(descPath))
                {
                    issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingDescTxt,
                        "desc.txt missing but m4b has Description tag",
                        parsed.Description, null));
                }
                else
                {
                    var descContent = File.ReadAllText(descPath);
                    if (!string.Equals(descContent, parsed.Description, StringComparison.Ordinal))
                    {
                        issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.IncorrectDescTxt,
                            "desc.txt content does not match Description tag",
                            parsed.Description, descContent));
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
                    issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingReaderTxt,
                        "reader.txt missing but m4b has Narrators tag",
                        expectedNarrators, null));
                }
                else
                {
                    var readerContent = File.ReadAllText(readerPath);
                    if (!string.Equals(readerContent, expectedNarrators, StringComparison.Ordinal))
                    {
                        issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.IncorrectReaderTxt,
                            "reader.txt content does not match Narrators tag",
                            expectedNarrators, readerContent));
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
                    issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                        "Cover file missing but m4b has embedded cover",
                        "cover.jpg or cover.png", null));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check consistency for {FilePath}", audiobook.FileInfoFullPath);
        }

        return issues;
    }
}
