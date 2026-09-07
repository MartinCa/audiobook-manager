using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class MetadataRefreshService : IMetadataRefreshService
{
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPendingMetadataRefreshRepository _pendingRepository;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IScrapingService _scrapingService;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly ILibrarySettingsRepository _librarySettingsRepository;
    private readonly ILogger<MetadataRefreshService> _logger;

    public MetadataRefreshService(
        IAudiobookRepository audiobookRepository,
        IPendingMetadataRefreshRepository pendingRepository,
        IConsistencyIssueRepository issueRepository,
        IScrapingService scrapingService,
        IEnumerable<IScraper> scrapers,
        ILibrarySettingsRepository librarySettingsRepository,
        ILogger<MetadataRefreshService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _pendingRepository = pendingRepository;
        _issueRepository = issueRepository;
        _scrapingService = scrapingService;
        _scrapers = scrapers;
        _librarySettingsRepository = librarySettingsRepository;
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

    public async Task<(int Processed, int Total, int Succeeded, int Failed)> RefreshStaleAudiobooksAsync(
        DateTime? olderThanUtc,
        Func<int, int, int, int, Task> progressAction)
    {
        var eligible = await _audiobookRepository.GetBooksEligibleForMetadataRefreshAsync(olderThanUtc);

        // URL support is not expressible in SQL (it lives in scraper code), so it filters here -
        // after the projection, so an unsupported URL costs a SupportsUrl call, not an entity load.
        var refreshable = eligible.Where(b => CanRefresh(b.Www)).ToList();

        var delayMs = Math.Max(0, (await _librarySettingsRepository.GetOrCreateAsync()).MetadataRefreshDelayMs);

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = refreshable.Count;
        string? stopReason = null;

        foreach (var book in refreshable)
        {
            if (stopReason is not null)
            {
                break;
            }

            if (processed > 0 && delayMs > 0)
            {
                await Task.Delay(delayMs);
            }

            try
            {
                var result = await RefreshAudiobookAsync(book.Id);
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
                _logger.LogWarning(ex, "Unexpected error refreshing audiobook {AudiobookId}", book.Id);
                failed++;
            }

            processed++;
            await progressAction(processed, total, succeeded, failed);
        }

        return (processed, total, succeeded, failed);
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

    public Task<(List<PendingMetadataRefresh> Items, int Total)> GetPendingPageAsync(int page, int pageSize) =>
        _pendingRepository.GetPageWithAudiobookAsync(page * pageSize, pageSize);

    public Task<List<long>> GetPendingAudiobookIdsAsync() =>
        _pendingRepository.GetPendingAudiobookIdsAsync();

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
        var staleIssue = existing.FirstOrDefault(i => i.IssueType == ConsistencyIssueType.MetadataRefreshFailed);

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
            await _issueRepository.InsertAsync(ConsistencyIssueFactory.Create(
                audiobookId,
                ConsistencyIssueType.MetadataRefreshFailed,
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