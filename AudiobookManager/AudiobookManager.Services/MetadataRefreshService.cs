using System.Text.Json;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>The outcome of <see cref="MetadataRefreshService.ReevaluatePendingRefreshesAsync"/>.</summary>
public record MetadataRefreshReevaluateResult(int Processed, int Updated, int Removed);

public class MetadataRefreshService : IMetadataRefreshService
{
    private static readonly JsonSerializerOptions ChangedFieldsJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPendingMetadataRefreshRepository _pendingRepository;
    private readonly IBookConsistencyIssueRepository _issueRepository;
    private readonly IScrapingService _scrapingService;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILibrarySettingsRepository _librarySettingsRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly IBookSeriesMapper _bookSeriesMapper;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<MetadataRefreshService> _logger;

    public MetadataRefreshService(
        IAudiobookRepository audiobookRepository,
        IPendingMetadataRefreshRepository pendingRepository,
        IBookConsistencyIssueRepository issueRepository,
        IScrapingService scrapingService,
        IEnumerable<IScraper> scrapers,
        ILibrarySettingsRepository librarySettingsRepository,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        IBookSeriesMapper bookSeriesMapper,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<MetadataRefreshService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _pendingRepository = pendingRepository;
        _issueRepository = issueRepository;
        _scrapingService = scrapingService;
        _scrapers = scrapers;
        _librarySettingsRepository = librarySettingsRepository;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
        _bookSeriesMapper = bookSeriesMapper;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public bool CanRefresh(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // The scrapers are the single authority on "which URL does this app understand" - the
        // eligibility check must move when a scraper is added or its URL support changes, not
        // drift behind a hardcoded domain list. A source whose API key is not configured cannot
        // answer either, so it does not make a book refreshable (same gate
        // GetSearchServiceInfo applies to the client's source picker).
        return _scrapers.Any(s =>
            s.SupportsUrl(url) && (!s.RequiresApiKey || s.IsApiKeyConfigured));
    }

    public async Task<MetadataRefreshResult> RefreshAudiobookAsync(long audiobookId)
    {
        var book = await _audiobookRepository.GetByIdWithIncludesAsync(audiobookId);
        if (book is null)
        {
            throw new KeyNotFoundException($"Audiobook {audiobookId} not found");
        }

        if (!CanRefresh(book.Www))
        {
            return new MetadataRefreshResult
            {
                Success = false,
                Error = "The book has no source URL that any configured metadata source supports.",
            };
        }

        try
        {
            var fetched = await _scrapingService.GetBookDetails(book.Www!);
            var differences = MetadataRefreshDiffer.Diff(book, fetched).ToList();
            await RecordFetchedSnapshotAsync(book, fetched, differences);
            return new MetadataRefreshResult
            {
                Success = true,
                HasDifferences = differences.Count > 0,
                Differences = differences,
                SourceName = fetched.Source,
            };
        }
        catch (HardcoverDailyLimitExceededException)
        {
            // Not an ordinary failure: the budget is spent and nothing will succeed until it
            // resets. Stamp nothing, raise nothing per-book - let the bulk loop stop early on
            // it (SeriesService.RunBulkAsync's precedent) and the single endpoint surface it.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Metadata refresh failed for audiobook {AudiobookId} ('{Title}') from '{Url}'",
                book.Id, book.BookName, book.Www);

            await RecordRefreshFailureAsync(book.Id, ex.Message);
            return new MetadataRefreshResult
            {
                Success = false,
                Error = ex.Message,
            };
        }
    }

    public async Task<MetadataRefreshBatchResult> RefreshStaleAudiobooksAsync(
        DateTime? olderThanUtc,
        Func<int, int, int, int, Task> progressAction)
    {
        var eligible = await _audiobookRepository.GetBooksEligibleForMetadataRefreshAsync(olderThanUtc);

        // URL support is not expressible in SQL (it lives in scraper code), so it filters here -
        // after the projection, so an unsupported URL costs a SupportsUrl call, not an entity load.
        var refreshable = eligible.Where(b => CanRefresh(b.Www)).ToList();

        return await RunRefreshLoopAsync(
            refreshable.Select(b => new RefreshTarget(b.Id, b.Www)).ToList(),
            progressAction);
    }

    public async Task<MetadataRefreshBatchResult> RefreshSelectedAudiobooksAsync(
        IReadOnlyList<long> audiobookIds,
        Func<int, int, int, int, Task> progressAction)
    {
        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(audiobookIds);

        // The user explicitly picked every id, so every id is a target - nothing is silently
        // dropped from the totals. Books that did not resolve become targets with no URL, and a
        // book with no refreshable URL is counted Failed ("3 of 5 had no source URL" is the
        // honest result) rather than filtered out the way the stale sweep below filters.
        var foundIds = new HashSet<long>();
        var targets = new List<RefreshTarget>();
        foreach (var book in books)
        {
            foundIds.Add(book.Id);
            targets.Add(new RefreshTarget(book.Id, book.Www));
        }

        foreach (var id in audiobookIds)
        {
            if (!foundIds.Contains(id))
            {
                targets.Add(new RefreshTarget(id, (string?)null));
            }
        }

        return await RunRefreshLoopAsync(targets, progressAction);
    }

    /// <summary>The per-book workload both bulk refresh paths run; the two must not drift apart.</summary>
    private sealed record RefreshTarget(long Id, string? Www);

    private async Task<MetadataRefreshBatchResult> RunRefreshLoopAsync(
        IReadOnlyList<RefreshTarget> targets,
        Func<int, int, int, int, Task> progressAction)
    {
        var delayMs = Math.Max(0, (await _librarySettingsRepository.GetOrCreateAsync()).MetadataRefreshDelayMs);

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = targets.Count;
        string? stopReason = null;

        foreach (var target in targets)
        {
            if (stopReason is not null)
            {
                break;
            }

            if (!CanRefresh(target.Www))
            {
                // Not refreshable costs no HTTP request and no inter-item delay, but it still
                // counts: the user explicitly picked this book, so "no source URL" is a result.
                failed++;
                processed++;
                await progressAction(processed, total, succeeded, failed);
                continue;
            }

            if (processed > 0 && delayMs > 0)
            {
                await Task.Delay(delayMs);
            }

            try
            {
                var result = await RefreshAudiobookAsync(target.Id);
                if (result.Success)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (HardcoverDailyLimitExceededException ex)
            {
                // This item was refused before it was attempted, not failed - don't count it as
                // processed, and don't bother reporting the ones after it (SeriesService.RunBulkAsync).
                _logger.LogWarning(ex,
                    "Stopping metadata refresh after {Processed}/{Total} books: {Message}",
                    processed, total, ex.Message);
                stopReason = "Hardcover daily API request limit reached";
                break;
            }
            catch (Exception ex)
            {
                // RefreshAudiobookAsync already recorded the failure issue; an unexpected error
                // here still counts as one failed book, not a batch abort.
                _logger.LogWarning(ex, "Unexpected error refreshing audiobook {AudiobookId}", target.Id);
                failed++;
            }

            processed++;
            await progressAction(processed, total, succeeded, failed);
        }

        return new MetadataRefreshBatchResult(processed, total, succeeded, failed, stopReason);
    }

    public async Task<bool> DismissPendingRefreshAsync(long audiobookId)
    {
        return await _pendingRepository.DeleteByAudiobookIdAsync(audiobookId);
    }

    public async Task<(PendingMetadataRefresh Row, PendingRefreshPayload.Snapshot Payload)?> GetPendingRefreshAsync(long audiobookId)
    {
        var row = await _pendingRepository.GetByAudiobookIdAsync(audiobookId);
        if (row is null)
        {
            return null;
        }

        var payload = PendingRefreshPayload.TryParse(row.PayloadJson);
        return payload is null ? null : (row, payload);
    }

    public async Task<(List<PendingMetadataRefresh> Items, int Total)> GetPendingPageAsync(
        int page, int pageSize, IReadOnlyCollection<string>? fieldsFilter = null)
    {
        // One-time cost per legacy row (a stored-row read, a book-with-includes read, and a
        // write), run sequentially on the request thread. Bounded by how many pending rows
        // predate ChangedFieldsJson and never recurs once they're all backfilled, but on a
        // library with a large pending set this first page load after upgrading can be
        // noticeably slower than every one after it.
        await EnsureChangedFieldsBackfilledAsync();

        if (fieldsFilter is null || fieldsFilter.Count == 0)
        {
            return await _pendingRepository.GetPageWithAudiobookAsync(page * pageSize, pageSize);
        }

        var matches = await GetFilterMatchesAsync(fieldsFilter);
        var total = matches.Count;
        var pageIds = matches.Skip(page * pageSize).Take(pageSize).Select(m => m.AudiobookId).ToList();

        var rows = await _pendingRepository.GetByAudiobookIdsWithAudiobookAsync(pageIds);
        var byId = rows.ToDictionary(r => r.AudiobookId);

        // GetByAudiobookIdsWithAudiobookAsync makes no ordering promise; re-impose the
        // FetchedAt-desc order GetFilterMatchesAsync already computed. A row can be legitimately
        // absent (dismissed/applied between the two reads) and is simply dropped rather than
        // failing the whole page.
        var items = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        return (items, total);
    }

    public async Task<List<long>> GetPendingAudiobookIdsAsync(IReadOnlyCollection<string>? fieldsFilter = null)
    {
        await EnsureChangedFieldsBackfilledAsync();

        if (fieldsFilter is null || fieldsFilter.Count == 0)
        {
            return await _pendingRepository.GetPendingAudiobookIdsAsync();
        }

        var matches = await GetFilterMatchesAsync(fieldsFilter);
        return matches.Select(m => m.AudiobookId).ToList();
    }

    /// <summary>
    /// Self-heals pending rows written before <c>ChangedFieldsJson</c> existed (it was added
    /// alongside the field filter/badges/selective-apply feature; every row a pre-existing
    /// library already had on disk predates it and has it stored as null). Recomputes each such
    /// row's changed fields by re-diffing its stored snapshot against the book exactly the way
    /// the write path would have, via <see cref="MetadataRefreshDiffer.DiffSnapshot"/>, and
    /// persists the result - or, if the book has since caught up with (or never actually
    /// differed from) the snapshot, supersedes the row the same way a fresh no-diff check does.
    /// A no-op once every row has been backfilled: the lookup that finds candidates is a cheap
    /// "is this column null" scan with no book graph attached - except for a row whose
    /// PayloadJson cannot be parsed, which is skipped every time rather than backfilled, so it
    /// never leaves that candidate list. That costs only a repeated cheap scan (its column stays
    /// null forever), not repeated book loads, so it is left as-is rather than papered over with
    /// a sentinel value that would then need its own "is this the corrupt-marker" handling.
    /// <para>
    /// The recompute is a re-diff against the book's CURRENT state, not a replay of what the
    /// original fetch recorded. If the book was edited after the snapshot was stored but before
    /// this backfill ever ran, that can add fields the original fetch never flagged (the book
    /// diverged further from the snapshot since) as well as drop ones it did (the book caught
    /// up). That is the intended behavior - a pending snapshot's job is to converge the book
    /// toward what the source last reported, and "which fields would still change it" is exactly
    /// what should be re-evaluated against the book as it is now, not as it was at fetch time.
    /// </para>
    /// </summary>
    private async Task EnsureChangedFieldsBackfilledAsync()
    {
        var missingIds = await _pendingRepository.GetAudiobookIdsMissingChangedFieldsAsync();
        if (missingIds.Count == 0)
        {
            return;
        }

        foreach (var audiobookId in missingIds)
        {
            var row = await _pendingRepository.GetByAudiobookIdAsync(audiobookId);
            if (row is null)
            {
                continue;
            }

            var payload = PendingRefreshPayload.TryParse(row.PayloadJson);
            if (payload is null)
            {
                // Corrupt/foreign payload - nothing to compute here; ApplyOneAsync already
                // surfaces this as an error if the row is ever applied.
                continue;
            }

            var book = await _audiobookRepository.GetByIdWithIncludesAsync(audiobookId);
            if (book is null)
            {
                continue;
            }

            var changedFields = MetadataRefreshDiffer.DiffSnapshot(book, payload).Select(d => d.Field).ToList();
            if (changedFields.Count == 0)
            {
                await _pendingRepository.DeleteByAudiobookIdAsync(audiobookId);
                continue;
            }

            await _pendingRepository.SetChangedFieldsJsonAsync(
                audiobookId, JsonSerializer.Serialize(changedFields, ChangedFieldsJsonOptions));
        }
    }

    /// <summary>
    /// Every pending row whose stored changed-fields are entirely contained in
    /// <paramref name="fieldsFilter"/>, newest-fetched first - the same subset rule and order
    /// both filtered read paths above share, computed once against the lightweight projection so
    /// filtering thousands of rows never loads a book graph.
    /// </summary>
    private async Task<List<PendingRefreshFieldsRow>> GetFilterMatchesAsync(IReadOnlyCollection<string> fieldsFilter)
    {
        var filterSet = new HashSet<string>(fieldsFilter);
        var all = await _pendingRepository.GetAllChangedFieldsAsync();

        return all
            .Where(row => MetadataRefreshFields.ParseChangedFieldsJson(row.ChangedFieldsJson) is { Count: > 0 } changed && changed.All(filterSet.Contains))
            .OrderByDescending(row => row.FetchedAt)
            .ThenBy(row => row.AudiobookId)
            .ToList();
    }

    public async Task<bool> ApplyPendingRefreshAsync(long audiobookId, IReadOnlyCollection<string>? fields = null)
    {
        var row = await _pendingRepository.GetByAudiobookIdAsync(audiobookId);
        if (row is null)
        {
            return false;
        }

        return await ApplyOneAsync(row, fields);
    }

    public Task<(int Processed, int Succeeded, int Failed)> ApplySelectedPendingRefreshesAsync(
        IReadOnlyList<long> audiobookIds, Func<int, int, int, int, Task> progressAction) =>
        ApplyManyAsync(audiobookIds, progressAction);

    public async Task<(int Processed, int Succeeded, int Failed)> ApplyFilteredPendingRefreshesAsync(
        IReadOnlyCollection<string> fieldsFilter, Func<int, int, int, int, Task> progressAction)
    {
        var matches = await GetFilterMatchesAsync(fieldsFilter);
        return await ApplyManyAsync(matches.Select(m => m.AudiobookId).ToList(), progressAction);
    }

    /// <summary>
    /// The shared "apply this book's full pending snapshot, tolerate per-book failure" loop
    /// behind both bulk-apply endpoints - explicit selection and filter-resolved - so the two
    /// cannot drift. A requested id with no pending row (already applied/dismissed since the
    /// client loaded it) counts as Failed rather than being silently dropped, mirroring
    /// RefreshSelectedAudiobooksAsync's "every requested id counts" contract.
    /// </summary>
    private async Task<(int Processed, int Succeeded, int Failed)> ApplyManyAsync(
        IReadOnlyList<long> audiobookIds, Func<int, int, int, int, Task> progressAction)
    {
        var rows = await _pendingRepository.GetByAudiobookIdsAsync(audiobookIds);
        var rowsById = rows.ToDictionary(r => r.AudiobookId);

        return await BulkOperationRunner.RunAsync(
            audiobookIds,
            async id =>
            {
                if (!rowsById.TryGetValue(id, out var row))
                {
                    throw new KeyNotFoundException($"Audiobook {id} has no pending metadata refresh.");
                }

                var applied = await ApplyOneAsync(row, fields: null);
                if (!applied)
                {
                    throw new KeyNotFoundException($"Audiobook {id} no longer exists.");
                }
            },
            _logger,
            id => $"Failed to apply pending metadata refresh for audiobook {id}",
            progressAction);
    }

    /// <summary>
    /// Applies one pending row's snapshot (the fields it recorded as changed, or the caller's
    /// explicit subset of them) to the live book and dismisses the row - the write path every
    /// apply entry point (single quick-apply, bulk-selected, bulk-filtered) funnels through, so
    /// they share one save-gate/recheck/dismiss sequence. Returns false only when the book no
    /// longer exists; a corrupt/unparseable payload throws instead of returning false, so a
    /// caller cannot confuse "nothing to apply" with "the stored snapshot is unreadable" - the
    /// single-book endpoint maps the throw to a 400 rather than silently reporting success.
    /// </summary>
    private async Task<bool> ApplyOneAsync(PendingMetadataRefresh row, IReadOnlyCollection<string>? fields)
    {
        var payload = PendingRefreshPayload.TryParse(row.PayloadJson);
        if (payload is null)
        {
            throw new InvalidOperationException(
                $"The pending metadata refresh for audiobook {row.AudiobookId} could not be read; its stored payload is not valid.");
        }

        var storedChangedFields = MetadataRefreshFields.ParseChangedFieldsJson(row.ChangedFieldsJson);

        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(new List<long> { row.AudiobookId });
        var dbBook = books.FirstOrDefault();

        // No explicit caller selection and nothing recorded: either this row predates
        // ChangedFieldsJson (an existing library's pending rows all do, until
        // EnsureChangedFieldsBackfilledAsync gets to them) or it is genuinely empty. Recompute
        // once from the stored snapshot rather than assuming "nothing to apply" - a caller can
        // reach this row through apply-selected/apply-filtered before any list view has had a
        // chance to backfill it.
        //
        // This reads dbBook before the save gate below is acquired, so a concurrent save could
        // in principle mutate the book between this diff and the gated apply, leaving the
        // recomputed field set stale by the time it's used. That is not a new race: the ordinary
        // path (stored fields applied against this same pre-gate book read) already has the
        // identical shape, so this recompute just inherits the existing window rather than
        // opening a new one.
        if ((fields is null || fields.Count == 0) && storedChangedFields.Count == 0 && dbBook is not null)
        {
            storedChangedFields = MetadataRefreshDiffer.DiffSnapshot(dbBook, payload).Select(d => d.Field).ToList();
        }

        var fieldsToApply = new HashSet<string>(fields is { Count: > 0 } ? fields : storedChangedFields);
        if (fieldsToApply.Count == 0)
        {
            // Either the book has genuinely caught up with the snapshot, or it no longer exists
            // (dbBook is null and there was nothing explicit to apply either way) - the row is
            // stale either way, so dismiss it rather than silently no-op forever.
            await _pendingRepository.DeleteByAudiobookIdAsync(row.AudiobookId);
            return true;
        }

        if (dbBook is null)
        {
            return false;
        }

        using var lease = _saveGate.Acquire(dbBook.Id);

        var domain = AudiobookService.FromDb(dbBook);
        domain.Id = dbBook.Id;
        MetadataRefreshApplier.ApplyFields(domain, payload, fieldsToApply);

        if (domain.Authors.Count == 0 || string.IsNullOrWhiteSpace(domain.BookName))
        {
            throw new InvalidOperationException(
                $"Applying the pending metadata refresh would leave audiobook {dbBook.Id} without an author or a title; the apply was refused.");
        }

        await _audiobookService.UpdateAudiobook(dbBook.Id, domain);

        try
        {
            // Resolved from a fresh scope, not injected, because ILibraryConsistencyService's own
            // constructor pulls in every IBookConsistencyIssueResolver - including the one that
            // depends on this service - and a direct constructor dependency here would make that
            // a circular service graph. IServiceScopeFactory has no such cycle: it is a singleton
            // that only reaches into the container at the point of use.
            using var scope = _serviceScopeFactory.CreateScope();
            var libraryConsistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            await libraryConsistencyService.RecheckAudiobookAsync(dbBook.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after applying a pending metadata refresh", dbBook.Id);
        }

        await _pendingRepository.DeleteByAudiobookIdAsync(dbBook.Id);
        return true;
    }

    /// <summary>
    /// Persists the fetch outcome. A snapshot with differences is stored for approval; a no-diff
    /// fetch only supersedes (deletes) any stale snapshot; a failure touches neither. In every
    /// success case the bookkeeping timestamp is stamped - "was it checked" is orthogonal to
    /// "did the user approve the result".
    /// </summary>
    private async Task RecordFetchedSnapshotAsync(
        Database.Models.Audiobook book,
        Scraping.Models.MetadataSearchResult fetched,
        List<MetadataRefreshDiff> differences)
    {
        if (differences.Count > 0)
        {
            await _pendingRepository.UpsertAsync(new PendingMetadataRefresh
            {
                AudiobookId = book.Id,
                FetchedAt = DateTime.UtcNow,
                SourceName = fetched.Source,
                SourceUrl = fetched.CleanUrl,
                PayloadJson = PendingRefreshPayload.Serialize(ToSnapshot(fetched)),
                ChangedFieldsJson = JsonSerializer.Serialize(differences.Select(d => d.Field).ToList(), ChangedFieldsJsonOptions),
            });
        }
        else
        {
            // A fresh no-diff check supersedes an older snapshot - the book now matches its
            // source, so any pending "would change these fields" row is out of date.
            await _pendingRepository.DeleteByAudiobookIdAsync(book.Id);
        }

        await _audiobookRepository.UpdateLastMetadataRefreshedAtAsync(book.Id, DateTime.UtcNow);

        _logger.LogInformation(
            "Metadata refresh for audiobook {AudiobookId} ('{Title}'): {DifferenceCount} differing fields",
            book.Id, book.BookName, differences.Count);
    }

    private async Task RecordRefreshFailureAsync(long audiobookId, string error)
    {
        // The bookkeeping timestamp is deliberately NOT stamped on failure: the next bulk run's
        // staleness filter then picks the book up again naturally (retry by omission).
        var existing = await _issueRepository.GetByAudiobookIdAsync(audiobookId);
        var staleIssue = existing.FirstOrDefault(i => i.IssueType == BookConsistencyIssueType.MetadataRefreshFailed);

        if (staleIssue is not null)
        {
            // Replace the previous error with this one - the issue is "the latest refresh
            // attempt failed", not a history.
            staleIssue.Description = "Metadata refresh failed";
            staleIssue.ActualValue = error;
            staleIssue.DetectedAt = DateTime.UtcNow;

            try
            {
                await _issueRepository.UpdateAsync(staleIssue);
            }
            catch (KeyNotFoundException)
            {
                // The stale issue was deleted between the read above and this update - a
                // concurrent resolve of the same book succeeded in the meantime. The row is
                // gone, which is at least as current as the error we were about to write;
                // bookkeeping only, so this must not fail the refresh attempt (UpdateAsync's
                // fail-fast would otherwise surface as a bogus 404 from the single-book
                // endpoint, which maps KeyNotFoundException to NotFound for the *book*).
            }
        }
        else
        {
            await _issueRepository.InsertAsync(BookConsistencyIssueFactory.Create(
                audiobookId,
                BookConsistencyIssueType.MetadataRefreshFailed,
                "Metadata refresh failed",
                expectedValue: null,
                actualValue: error));
        }
    }

    private static PendingRefreshPayload.Snapshot ToSnapshot(Scraping.Models.MetadataSearchResult fetched) => new(
        PendingRefreshPayload.CurrentVersion,
        fetched.CleanUrl,
        fetched.Source,
        fetched.Authors.Select(a => a.Name).ToList(),
        fetched.Narrators.Select(n => n.Name).ToList(),
        fetched.BookName,
        fetched.Subtitle,
        fetched.Series?.FirstOrDefault()?.SeriesName,
        fetched.Series?.FirstOrDefault()?.SeriesPart,
        fetched.Year,
        fetched.Genres.ToList(),
        fetched.Description,
        fetched.Language,
        fetched.Rating?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        fetched.Copyright,
        fetched.Publisher,
        fetched.Asin,
        fetched.Series?.FirstOrDefault()?.OriginalSeriesName ?? fetched.Series?.FirstOrDefault()?.SeriesName);

    /// <summary>
    /// Re-evaluates every pending snapshot against the library and mapping rules as they stand
    /// right now, without re-scraping anything. Two things can make a stored snapshot stale
    /// besides the book itself changing:
    /// <list type="bullet">
    /// <item>A series mapping pattern was added or edited after the snapshot was captured. The
    /// stored <see cref="PendingRefreshPayload.Snapshot.SeriesName"/> is whatever
    /// <see cref="IBookSeriesMapper"/> produced at fetch time and is never revisited on its own -
    /// see the class remarks on <see cref="PendingRefreshPayload.Snapshot.OriginalSeriesName"/>.
    /// This re-runs the mapper against that original (pre-mapping) name with today's patterns and
    /// rewrites the stored snapshot when the mapped name changes - including a row predating that
    /// field (<c>OriginalSeriesName</c> null), which falls back to the stored
    /// <c>SeriesName</c> itself (see <see cref="RemapSeriesAsync"/> for why that fallback is
    /// correct, not a guess).</item>
    /// <item>The changed-fields list itself can simply be out of date - the same recompute
    /// <see cref="EnsureChangedFieldsBackfilledAsync"/> performs for legacy rows, applied to every
    /// row rather than only ones missing the column.</item>
    /// </list>
    /// A row that no longer differs from its book (the book caught up, or remapping made the
    /// series agree) is deleted rather than left to linger as a no-op. Pure DB/CPU work - no
    /// scraper is contacted - so this runs synchronously rather than through
    /// <c>BackgroundOperationRunner</c>.
    /// </summary>
    public async Task<MetadataRefreshReevaluateResult> ReevaluatePendingRefreshesAsync()
    {
        var ids = await _pendingRepository.GetPendingAudiobookIdsAsync();
        if (ids.Count == 0)
        {
            return new MetadataRefreshReevaluateResult(0, 0, 0);
        }

        var rows = await _pendingRepository.GetByAudiobookIdsAsync(ids);
        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(ids);
        var booksById = books.ToDictionary(b => b.Id);

        var processed = 0;
        var updated = 0;
        var removed = 0;

        foreach (var row in rows)
        {
            processed++;

            var payload = PendingRefreshPayload.TryParse(row.PayloadJson);
            if (payload is null)
            {
                // Corrupt/foreign payload - left alone, same as the self-heal backfill; apply
                // already surfaces this as an error if the row is ever applied.
                continue;
            }

            if (!booksById.TryGetValue(row.AudiobookId, out var book))
            {
                await _pendingRepository.DeleteByAudiobookIdAsync(row.AudiobookId);
                removed++;
                continue;
            }

            var remapped = await RemapSeriesAsync(payload);

            var diffs = MetadataRefreshDiffer.DiffSnapshot(book, remapped).ToList();
            if (diffs.Count == 0)
            {
                await _pendingRepository.DeleteByAudiobookIdAsync(row.AudiobookId);
                removed++;
                continue;
            }

            var changedFieldsJson = JsonSerializer.Serialize(diffs.Select(d => d.Field).ToList(), ChangedFieldsJsonOptions);
            var payloadJson = PendingRefreshPayload.Serialize(remapped);

            if (!string.Equals(payloadJson, row.PayloadJson, StringComparison.Ordinal) ||
                !string.Equals(changedFieldsJson, row.ChangedFieldsJson, StringComparison.Ordinal))
            {
                await _pendingRepository.UpdatePayloadAndChangedFieldsAsync(row.AudiobookId, payloadJson, changedFieldsJson);
                updated++;
            }
        }

        return new MetadataRefreshReevaluateResult(processed, updated, removed);
    }

    /// <summary>
    /// Re-runs the series mapper against a snapshot's pre-mapping series name with the mapping
    /// patterns as they exist right now, returning the snapshot unchanged when there is nothing to
    /// remap (no series at all) or when the mapped name did not change.
    ///
    /// A row written before <c>OriginalSeriesName</c> existed has it stored as null - falls back
    /// to the already-recorded <see cref="PendingRefreshPayload.Snapshot.SeriesName"/> as the
    /// remap input for such a row rather than skipping it. That fallback is not a guess: at fetch
    /// time no mapping pattern matched this book's series (if one had, the user would not be
    /// adding a new pattern for it now), so <see cref="IBookSeriesMapper.MapSingleBookSeries"/>
    /// returned the cleaned name unchanged - the stored <c>SeriesName</c> on such a row already
    /// IS the pre-mapping name, it was just never labeled as such. Skipping the fallback here left
    /// every pending row that predates this feature permanently un-remappable by re-evaluation,
    /// which defeated the point for exactly the rows a user is most likely to want fixed.
    /// </summary>
    private async Task<PendingRefreshPayload.Snapshot> RemapSeriesAsync(PendingRefreshPayload.Snapshot payload)
    {
        var sourceName = payload.OriginalSeriesName ?? payload.SeriesName;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return payload;
        }

        var mapped = await _bookSeriesMapper.MapBookSeries(new List<MetadataSeriesSearchResult>
        {
            new(sourceName) { SeriesPart = payload.SeriesPart },
        });
        var mappedName = mapped[0].SeriesName;

        return string.Equals(mappedName, payload.SeriesName, StringComparison.Ordinal)
            ? payload
            : payload with { SeriesName = mappedName };
    }
}
