using System.Text.Json;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

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
        if (fieldsFilter is null || fieldsFilter.Count == 0)
        {
            return await _pendingRepository.GetPendingAudiobookIdsAsync();
        }

        var matches = await GetFilterMatchesAsync(fieldsFilter);
        return matches.Select(m => m.AudiobookId).ToList();
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
        var fieldsToApply = new HashSet<string>(fields is { Count: > 0 } ? fields : storedChangedFields);
        if (fieldsToApply.Count == 0)
        {
            // Nothing recorded to apply (an old row from before ChangedFieldsJson existed, with
            // no explicit fields given either) - dismiss it rather than silently no-op forever.
            await _pendingRepository.DeleteByAudiobookIdAsync(row.AudiobookId);
            return true;
        }

        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(new List<long> { row.AudiobookId });
        var dbBook = books.FirstOrDefault();
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
        fetched.Asin);
}