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
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ILogger<LibraryConsistencyService> _logger;

    public LibraryConsistencyService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        IOrphanDirectoryRepository orphanDirectoryRepository,
        IAudiobookTagHandler tagHandler,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        ILogger<LibraryConsistencyService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _orphanDirectoryRepository = orphanDirectoryRepository;
        _tagHandler = tagHandler;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
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

        // A single recursive enumeration, walked deepest-first.
        //
        // Checking only leaf directories (which this used to do) meant a deleted series was
        // cleaned up one level per run: the check flagged "Author/Series/Book", resolving it
        // deleted that folder, and only the *next* full check noticed "Author/Series" had become
        // a leaf - so the user had to run the check once per level. Bottom-up, a directory whose
        // every subdirectory is itself being reclaimed is reported in the same pass.
        //
        // "Does this subtree hold audio?" is answered from the children's already-computed
        // answers rather than by re-walking the subtree, so every file in the library is
        // stat'ed once for the whole sweep. Asking Directory.EnumerateFiles(dir, "*",
        // AllDirectories) per directory would re-walk each file once per ancestor level.
        var allDirectories = Directory
            .EnumerateDirectories(_settings.AudiobookLibraryPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(directory => directory.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ToList();

        var subtreeHasAudio = new HashSet<string>(AudiobookFileHandler.PathComparer);
        var orphans = new List<OrphanDirectory>();
        var orphansByPath = new Dictionary<string, OrphanDirectory>(AudiobookFileHandler.PathComparer);

        foreach (var directory in allDirectories)
        {
            var subdirectories = Directory.EnumerateDirectories(directory).ToList();

            var hasAudioFile =
                Directory.EnumerateFiles(directory).Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)))
                || subdirectories.Any(subtreeHasAudio.Contains);

            if (hasAudioFile)
            {
                subtreeHasAudio.Add(directory);
                continue;
            }

            // Nothing under here is audio, so the whole subtree is reclaimable. Report only this
            // directory: deleting it removes the children anyway, and listing both would make
            // the user resolve the same folder twice.
            foreach (var subdirectory in subdirectories)
            {
                if (orphansByPath.Remove(subdirectory, out var superseded))
                {
                    orphans.Remove(superseded);
                    issuesFound--;
                }
            }

            var orphan = new OrphanDirectory
            {
                DirectoryPath = directory,
                DetectedAt = DateTime.UtcNow
            };
            orphans.Add(orphan);
            orphansByPath[directory] = orphan;
            issuesFound++;
        }

        // One insert for the whole sweep rather than a SaveChanges per orphaned folder.
        await _orphanDirectoryRepository.InsertRangeAsync(orphans);

        await progressAction("Checked library directories for orphaned folders", totalBooks, totalBooks, issuesFound);

        return issuesFound;
    }

    /// <summary>
    /// How far a resolve reaches beyond the issue it was asked to fix. Resolving one issue
    /// routinely deletes other stored issues for the same book, and a bulk resolve has to know
    /// which of its remaining items that already took care of - see
    /// <see cref="ResolveLoadedIssuesAsync"/>.
    /// </summary>
    private enum ResolveScope
    {
        /// <summary>Only this issue row was removed.</summary>
        IssueOnly,

        /// <summary>
        /// Every stored issue for the book was removed - the path or the tags changed (or the
        /// book was deleted outright), which invalidates every other check for it.
        /// </summary>
        AllForAudiobook,

        /// <summary>
        /// The book's sidecar-content issues were all removed, because WriteMetadata rewrites
        /// desc.txt, reader.txt and metadata.opf together.
        /// </summary>
        SidecarsForAudiobook,
    }

    private static readonly ConsistencyIssueType[] _sidecarIssueTypes =
    {
        ConsistencyIssueType.MissingDescTxt, ConsistencyIssueType.IncorrectDescTxt,
        ConsistencyIssueType.MissingReaderTxt, ConsistencyIssueType.IncorrectReaderTxt,
        ConsistencyIssueType.MissingOpfFile, ConsistencyIssueType.IncorrectOpfFile
    };

    private static bool IsSidecarIssue(ConsistencyIssueType issueType) =>
        Array.IndexOf(_sidecarIssueTypes, issueType) >= 0;

    public async Task<ConsistencyResolveResult> ResolveIssue(long issueId)
    {
        var issue = await _issueRepository.GetByIdAsync(issueId);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found");

        var (_, result) = await ResolveLoadedIssue(issue);
        return result;
    }

    /// <summary>
    /// Resolving an issue rewrites the book's tags, moves its file, or rewrites its sidecars, so
    /// it takes the same per-audiobook gate an interactive save does. Nothing below this point
    /// takes it again - the gate is non-reentrant, so it is held here, once, for whichever
    /// handler runs. A book that is busy fails just this issue: the callers' per-item try/catch
    /// counts it and carries on, and the next check picks the issue up again.
    /// </summary>
    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveLoadedIssue(ConsistencyIssue issue)
    {
        using var lease = _saveGate.Acquire(issue.AudiobookId);

        return await ResolveLoadedIssueCore(issue);
    }

    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveLoadedIssueCore(ConsistencyIssue issue)
    {
        switch (issue.IssueType)
        {
            case ConsistencyIssueType.MissingMediaFile:
                return await ResolveMissingMediaFile(issue);

            case ConsistencyIssueType.MissingDescTxt:
            case ConsistencyIssueType.IncorrectDescTxt:
            case ConsistencyIssueType.MissingReaderTxt:
            case ConsistencyIssueType.IncorrectReaderTxt:
            case ConsistencyIssueType.MissingOpfFile:
            case ConsistencyIssueType.IncorrectOpfFile:
                return await ResolveMetadataIssue(issue);

            case ConsistencyIssueType.MissingCoverFile:
                return await ResolveMissingCover(issue);

            case ConsistencyIssueType.WrongFilePath:
            case ConsistencyIssueType.TagMismatch:
                return await ResolveTagOrPathMismatch(issue);

            default:
                return (ResolveScope.IssueOnly, new ConsistencyResolveResult(issue.Id, issue.IssueType, "resolved", "Issue resolved."));
        }
    }

    /// <summary>
    /// Resolves an already-loaded batch, tolerating the cascades the handlers perform.
    ///
    /// Resolving one issue deletes other stored issues for the same book - a path change or a
    /// tag rewrite invalidates every check for it, and writing the sidecars fixes all three at
    /// once - so an item later in the batch is routinely already gone by the time the loop
    /// reaches it. Re-fetching it by id (as this used to) found nothing and threw
    /// KeyNotFoundException, which BulkOperationRunner counted as a failure: a batch that
    /// succeeded completely reported "Resolved 1 issues (1 failed)". Those entries are skipped
    /// instead, and counted in neither total.
    ///
    /// The scope each handler reports is what drives the skipping, so the cascade rules stay in
    /// the handlers that perform them rather than being restated here (or, worse, in the client).
    /// </summary>
    private async Task<(int resolved, int failed)> ResolveLoadedIssuesAsync(IReadOnlyList<ConsistencyIssue> issues)
    {
        var succeeded = 0;
        var failed = 0;
        var cascadedAll = new HashSet<long>();
        var cascadedSidecars = new HashSet<long>();

        foreach (var issue in issues)
        {
            if (cascadedAll.Contains(issue.AudiobookId) ||
                (cascadedSidecars.Contains(issue.AudiobookId) && IsSidecarIssue(issue.IssueType)))
            {
                _logger.LogDebug(
                    "Skipping issue {IssueId} ({IssueType}): an earlier resolve in this batch already covered audiobook {AudiobookId}",
                    issue.Id, issue.IssueType, issue.AudiobookId);
                continue;
            }

            try
            {
                var (scope, _) = await ResolveLoadedIssue(issue);
                succeeded++;

                if (scope == ResolveScope.AllForAudiobook)
                {
                    cascadedAll.Add(issue.AudiobookId);
                }
                else if (scope == ResolveScope.SidecarsForAudiobook)
                {
                    cascadedSidecars.Add(issue.AudiobookId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve issue {IssueId} during bulk resolve", issue.Id);
                failed++;
            }
        }

        return (succeeded, failed);
    }

    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveMissingMediaFile(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;

        if (File.Exists(audiobook.FileInfoFullPath))
        {
            _logger.LogInformation(
                "Media file for audiobook {AudiobookId} ('{Title}') found at '{FilePath}'. Preserving audiobook and re-evaluating consistency.",
                audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

            await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
            var newIssues = await Task.Run(() => DetectIssuesForAudiobook(audiobook));
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
            AudiobookFileHandler.RemoveDirIfEmpty(directoryPath);
        }

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "audiobook_deleted",
            "Media file not found. Audiobook record deleted from library."));
    }

    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveMetadataIssue(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        AudiobookFileHandler.WriteMetadata(parsed);

        _logger.LogInformation(
            "Rewrote metadata sidecars (desc.txt, reader.txt, metadata.opf) for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            audiobook.Id, audiobook.BookName, audiobook.FileInfoFullPath);

        // Delete all desc/reader/opf issues for this book since WriteMetadata writes all three
        await _issueRepository.DeleteByAudiobookIdAndTypesAsync(audiobook.Id, _sidecarIssueTypes);

        return (ResolveScope.SidecarsForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Metadata sidecar files updated."));
    }

    /// <summary>
    /// Handles both TagMismatch and WrongFilePath. Both are symptoms of the same thing - the file
    /// on disk doesn't match the library metadata already in the database - so both are fixed the
    /// same way: rewrite the m4b tags from the database and let the normal save pipeline relocate
    /// the file and resync its sidecars if the path changes too. See the "Binding invariant" in
    /// CLAUDE.md - UpdateAudiobook is the only place allowed to touch these fields.
    ///
    /// This used to be two handlers. ResolveWrongFilePath re-parsed tags from the file itself
    /// (assuming they were already correct) and only relocated it, then deleted every issue for
    /// the book on success - including a TagMismatch it had never touched, so a coexisting tag
    /// mismatch silently vanished from the list without ever being fixed. Going through the same
    /// database-is-truth pipeline as TagMismatch always did closes that gap by construction:
    /// there's no "assume tags are fine" path left to desync from what actually got resolved.
    /// </summary>
    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveTagOrPathMismatch(ConsistencyIssue issue)
    {
        var dbAudiobook = await _audiobookRepository.GetByIdWithIncludesAsync(issue.AudiobookId);
        if (dbAudiobook == null)
            throw new KeyNotFoundException($"Audiobook {issue.AudiobookId} not found");

        // The per-audiobook gate is already held by ResolveLoadedIssue.
        var domain = AudiobookService.FromDb(dbAudiobook);
        await _audiobookService.UpdateAudiobook(issue.AudiobookId, domain);

        _logger.LogInformation(
            "Rewrote tags and aligned file path for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            issue.AudiobookId, dbAudiobook.BookName, dbAudiobook.FileInfoFullPath);

        // Tags (and potentially the file path) changed, invalidating all other checks for this book
        await _issueRepository.DeleteByAudiobookIdAsync(issue.AudiobookId);

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Tags and file path updated."));
    }

    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveMissingCover(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var coverPath = AudiobookFileHandler.WriteCover(parsed);
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

    public async Task ResolveOrphanDirectory(long orphanDirectoryId)
    {
        var directory = await _orphanDirectoryRepository.GetByIdAsync(orphanDirectoryId);
        if (directory == null)
            throw new KeyNotFoundException($"Orphan directory {orphanDirectoryId} not found");

        DeleteOrphanDirectoryFromDisk(directory.DirectoryPath);
        _logger.LogInformation("Deleted orphan directory from disk: '{DirectoryPath}'", directory.DirectoryPath);
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

        // GetByTypeAsync already returns each issue with the audiobook graph the resolve
        // handlers need, so resolve those entities directly instead of throwing them away and
        // re-fetching each one by id (which cost N+1 queries with the same includes).
        var issues = await _issueRepository.GetByTypeAsync(parsedType);

        return await ResolveLoadedIssuesAsync(issues);
    }

    public async Task<(int resolved, int failed)> ResolveIssues(IEnumerable<long> issueIds)
    {
        var issues = await _issueRepository.GetByIdsAsync(issueIds.ToList());

        return await ResolveLoadedIssuesAsync(issues);
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

            // Check desc.txt. The "no tag at all" branch matters as much as the others: these
            // sidecars are generated from the tags and take precedence over them in
            // Audiobookshelf, so one left behind after its field was cleared is a file actively
            // serving stale metadata. It used to be invisible here - the whole check was skipped
            // when the tag was empty - and WriteMetadata never rewrote it either, so it survived
            // every save and every consistency run.
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
            else if (File.Exists(descPath))
            {
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.IncorrectDescTxt,
                    "desc.txt present but m4b has no Description tag",
                    null, File.ReadAllText(descPath)));
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
            else if (File.Exists(readerPath))
            {
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.IncorrectReaderTxt,
                    "reader.txt present but m4b has no Narrators tag",
                    null, File.ReadAllText(readerPath)));
            }

            // Check metadata.opf - unlike desc.txt/reader.txt, this is expected unconditionally
            // once the book has tags at all, since it always carries at least the title/authors.
            var opfPath = AudiobookFileHandler.JoinPaths(directoryPath, "metadata.opf");
            var expectedOpfContent = AudiobookFileHandler.BuildOpfContent(parsed);
            if (!File.Exists(opfPath))
            {
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingOpfFile,
                    "metadata.opf missing",
                    expectedOpfContent, null));
            }
            else
            {
                var opfContent = File.ReadAllText(opfPath);
                if (!string.Equals(opfContent, expectedOpfContent, StringComparison.Ordinal))
                {
                    issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.IncorrectOpfFile,
                        "metadata.opf content does not match library metadata",
                        expectedOpfContent, opfContent));
                }
            }

            // Check cover file
            var coverJpgExists = File.Exists(AudiobookFileHandler.JoinPaths(directoryPath, "cover.jpg"));
            var coverPngExists = File.Exists(AudiobookFileHandler.JoinPaths(directoryPath, "cover.png"));

            if (coverJpgExists && coverPngExists)
            {
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                    "Conflicting cover files (both cover.jpg and cover.png exist)",
                    parsed.Cover?.MimeType == "image/png" ? "cover.png" : "cover.jpg",
                    "both cover.jpg and cover.png exist"));
            }
            else if (parsed.Cover is not null && !coverJpgExists && !coverPngExists)
            {
                issues.Add(BuildIssue(audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                    "Cover file missing but m4b has embedded cover",
                    "cover.jpg or cover.png", null));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check consistency for {FilePath}", audiobook.FileInfoFullPath);
        }

        return issues;
    }
}
