using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class LibraryConsistencyService : ILibraryConsistencyService
{
    /// <summary>Issues accumulated before a single batched insert.</summary>
    private const int InsertBatchSize = 500;

    /// <summary>Books checked between SignalR progress broadcasts during a full check.</summary>
    private const int ProgressBroadcastInterval = 25;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookService _audiobookService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly IAudiobookIssueDetectionService _detectionService;
    private readonly IOrphanDirectoryConsistencyService _orphanDirectoryConsistencyService;
    private readonly Dictionary<ConsistencyIssueType, IConsistencyIssueResolver> _resolversByType;
    private readonly ILogger<LibraryConsistencyService> _logger;

    public LibraryConsistencyService(
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        IAudiobookTagHandler tagHandler,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        IAudiobookIssueDetectionService detectionService,
        IEnumerable<IConsistencyIssueResolver> resolvers,
        IOrphanDirectoryConsistencyService orphanDirectoryConsistencyService,
        ILogger<LibraryConsistencyService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _tagHandler = tagHandler;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
        _detectionService = detectionService;
        _orphanDirectoryConsistencyService = orphanDirectoryConsistencyService;
        _logger = logger;

        // Every ConsistencyIssueType must resolve to exactly one registered resolver. Checked here,
        // eagerly, rather than only at dispatch time (see ResolveLoadedIssueCore): the type is a
        // closed, statically known enum, so a forgotten resolver is a wiring bug that should fail
        // the moment this service is constructed, not surface later as one counted failure buried
        // in a user's bulk resolve.
        _resolversByType = resolvers
            .SelectMany(resolver => resolver.HandledTypes.Select(type => (Type: type, Resolver: resolver)))
            .ToDictionary(x => x.Type, x => x.Resolver);

        var unhandledTypes = Enum.GetValues<ConsistencyIssueType>().Except(_resolversByType.Keys).ToList();
        if (unhandledTypes.Count > 0)
        {
            throw new InvalidOperationException(
                $"No consistency issue resolver registered for: {string.Join(", ", unhandledTypes)}");
        }
    }

    public async Task<(int BooksChecked, int IssuesFound)> RunConsistencyCheck(Func<string, int, int, int, Task> progressAction)
    {
        _logger.LogInformation("Starting library consistency check");

        // Both tables are cleared up front, before any work starts: if detection or an insert
        // fails partway through the loop below, the issue and orphan-directory lists must not be
        // left describing two different points in time (the issue table wiped by a run that never
        // reached the orphan sweep, with the orphan table still holding the previous run's rows).
        await _issueRepository.ClearAllAsync();
        await _orphanDirectoryConsistencyService.ClearAllAsync();

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

            var issues = _detectionService.DetectIssues(audiobook);
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

        issuesFound = await _orphanDirectoryConsistencyService.ScanAsync(progressAction, totalBooks, issuesFound);

        _logger.LogInformation("Consistency check complete. Books: {Total}, Issues: {Issues}", totalBooks, issuesFound);

        return (totalBooks, issuesFound);
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
    /// Returns the differing tag fields for a TagMismatch issue as a list of (field, library
    /// value, file value) rows for the selective resolution screen. Only fields that actually
    /// differ are returned. Throws if the issue is not a TagMismatch.
    /// </summary>
    public async Task<List<TagMismatchField>> GetTagMismatchFieldsAsync(long issueId)
    {
        var issue = await _issueRepository.GetByIdAsync(issueId);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found");
        if (issue.IssueType != ConsistencyIssueType.TagMismatch)
            throw new ArgumentException($"Issue {issueId} is not a TagMismatch");

        var dbAudiobook = issue.Audiobook;
        var domain = AudiobookService.FromDb(dbAudiobook);

        // Detection is blocking work (an ATL parse of the whole m4b), so keep it off the request
        // thread, mirroring DetectIssuesForAudiobookAsync.
        var parsed = await Task.Run(() =>
            _tagHandler.ParseAudiobook(new FileInfo(dbAudiobook.FileInfoFullPath), includeCoverData: false));

        return TagConsistencyChecker.FindMismatches(domain, parsed)
            .Select(m => new TagMismatchField(m.Field, m.Expected, m.Actual))
            .ToList();
    }

    /// <summary>
    /// Applies the user's per-field choices to resolve a TagMismatch. For each differing field the
    /// caller supplies a chosen serialized value (library value, file value, or empty to clear);
    /// omitted fields keep the library metadata. The merged audiobook is persisted through
    /// <see cref="AudiobookService.UpdateAudiobook"/>, so the m4b tags, library path, and sidecars
    /// all stay consistent — this is the same binding-invariant pipeline a normal save uses.
    /// </summary>
    public async Task<ConsistencyResolveResult> ResolveTagMismatchAsync(long issueId, IReadOnlyDictionary<string, string?> fieldValues)
    {
        var issue = await _issueRepository.GetByIdAsync(issueId);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found");
        if (issue.IssueType != ConsistencyIssueType.TagMismatch)
            throw new ArgumentException($"Issue {issueId} is not a TagMismatch");

        var dbAudiobook = issue.Audiobook;
        var domain = AudiobookService.FromDb(dbAudiobook);

        foreach (var (field, value) in fieldValues)
        {
            TagMismatchFields.ApplyValue(domain, field, value);
        }

        // The per-audiobook gate is held here, once, around the whole rewrite (the same scope a
        // ResolveTagOrPathMismatch resolve takes). Nothing below re-acquires it.
        using var lease = _saveGate.Acquire(dbAudiobook.Id);

        await _audiobookService.UpdateAudiobook(dbAudiobook.Id, domain);

        _logger.LogInformation(
            "Resolved tag mismatch selectively for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            dbAudiobook.Id, dbAudiobook.BookName, dbAudiobook.FileInfoFullPath);

        // Tags (and potentially the path) changed, invalidating every other check for this book.
        await _issueRepository.DeleteByAudiobookIdAsync(dbAudiobook.Id);

        return new ConsistencyResolveResult(
            issue.Id,
            ConsistencyIssueType.TagMismatch,
            "resolved",
            "Selected tag values applied and file path updated.");
    }

    /// <summary>
    /// Resolving an issue rewrites the book's tags, moves its file, or rewrites its sidecars, so
    /// it takes the same per-audiobook gate an interactive save does. Nothing below this point
    /// takes it again - the gate is non-reentrant, so it is held here, once, for whichever
    /// resolver runs. A book that is busy fails just this issue: the callers' per-item try/catch
    /// counts it and carries on, and the next check picks the issue up again.
    /// </summary>
    private async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveLoadedIssue(ConsistencyIssue issue)
    {
        using var lease = _saveGate.Acquire(issue.AudiobookId);

        return await ResolveLoadedIssueCore(issue);
    }

    private Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveLoadedIssueCore(ConsistencyIssue issue)
    {
        if (!_resolversByType.TryGetValue(issue.IssueType, out var resolver))
            throw new InvalidOperationException($"No consistency issue resolver registered for {issue.IssueType}");

        return resolver.ResolveAsync(issue);
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
    /// The scope each resolver reports is what drives the skipping, so the cascade rules stay in
    /// the resolvers that perform them rather than being restated here (or, worse, in the client).
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

    public Task<OrphanDirectoryResolveResult> ResolveOrphanDirectory(long orphanDirectoryId) =>
        _orphanDirectoryConsistencyService.ResolveOrphanDirectory(orphanDirectoryId);

    public Task<(int resolved, int failed, int retained)> ResolveAllOrphanDirectories() =>
        _orphanDirectoryConsistencyService.ResolveAllOrphanDirectories();

    public async Task<(int resolved, int failed)> ResolveIssuesByType(string issueType)
    {
        if (!Enum.TryParse<ConsistencyIssueType>(issueType, out var parsedType))
            throw new ArgumentException($"Unknown issue type: {issueType}");

        // GetByTypeAsync already returns each issue with the audiobook graph the resolvers need,
        // so resolve those entities directly instead of throwing them away and re-fetching each
        // one by id (which cost N+1 queries with the same includes).
        var issues = await _issueRepository.GetByTypeAsync(parsedType);

        return await ResolveLoadedIssuesAsync(issues);
    }

    public async Task<(int resolved, int failed)> ResolveIssues(IEnumerable<long> issueIds)
    {
        var issues = await _issueRepository.GetByIdsAsync(issueIds.ToList());

        return await ResolveLoadedIssuesAsync(issues);
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
    /// the rest of the table must be left alone; the full check uses
    /// <see cref="IAudiobookIssueDetectionService"/> directly with batched inserts instead, since
    /// it already cleared the table up front.
    /// </summary>
    private async Task<List<ConsistencyIssue>> DetectIssuesForAudiobookAsync(Audiobook audiobook)
    {
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);

        // Detection is blocking work - an ATL parse of the whole m4b, plus several file reads -
        // and this path is awaited directly by a controller action, so it must not run on the
        // request thread. (The full check needs no such wrapper: BackgroundOperationRunner
        // already puts it on the thread pool.)
        var issues = await Task.Run(() => _detectionService.DetectIssues(audiobook));

        await _issueRepository.InsertRangeAsync(issues);
        return issues;
    }
}
