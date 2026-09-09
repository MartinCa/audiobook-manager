using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MetadataRefreshController : ControllerBase
{
    /// <summary>The largest page a caller may ask for. Beyond this the response stops being a page.</summary>
    private const int MaxPageSize = 200;

    private const int DefaultPageSize = 50;

    /// <summary>
    /// The furthest into the list a caller may ask to start - the same overflow/negative-OFFSET
    /// guard UrlCleanupController documents (page * pageSize overflows int past ~10.7M pages,
    /// and SQLite reads a negative OFFSET as zero, silently serving the first page).
    /// </summary>
    private const long MaxPageOffset = 1_000_000;

    public const string BulkOperationKey = "metadata-refresh";

    private static readonly SemaphoreSlim _bulkLock = new(1, 1);

    private readonly IMetadataRefreshService _metadataRefreshService;
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<MetadataRefreshController> _logger;

    public MetadataRefreshController(
        IMetadataRefreshService metadataRefreshService,
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IHostApplicationLifetime appLifetime,
        ILogger<MetadataRefreshController> logger)
    {
        _metadataRefreshService = metadataRefreshService;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes one book from its source URL, synchronously. The fetch is bounded by the
    /// scraper's own HTTP timeouts, the same latency profile metadata-search/details already has.
    /// </summary>
    [HttpPost("{id:long}")]
    public async Task<ActionResult<MetadataRefreshResultDto>> RefreshAudiobook(long id)
    {
        try
        {
            var result = await _metadataRefreshService.RefreshAudiobookAsync(id);
            return Ok(ToDto(result));
        }
        catch (KeyNotFoundException)
        {
            // The id is the whole message - the caller supplied it and can see it.
            return NotFound();
        }
        catch (Scraping.RateLimiting.HardcoverDailyLimitExceededException ex)
        {
            // 4xx detail is relayed to the user by design; this message names the cause and the
            // remedy without leaking anything about the environment.
            return this.InvalidRequest(ex.Message);
        }
    }

    /// <summary>
    /// Refreshes every eligible book (URL a configured source supports; never refreshed, or last
    /// refreshed before <paramref name="dto"/>'s cutoff). Fire-and-forget through
    /// <see cref="BackgroundOperationRunner"/> with SignalR progress, the same shape as the
    /// series refresh and consistency resolve bulk endpoints.
    /// </summary>
    [HttpPost("bulk")]
    public IActionResult StartBulkRefresh([FromBody] BulkMetadataRefreshDto dto)
    {
        return BackgroundOperationRunner.Start(
            _bulkLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            BulkOperationKey,
            async sp =>
            {
                var refreshService = sp.GetRequiredService<IMetadataRefreshService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(BulkOperationKey, processed, total);
                    return _organizeHub.Clients.All.MetadataRefreshProgress(
                        new MetadataRefreshProgress(processed, total, succeeded, failed));
                }

                // RefreshAudiobookAsync never throws for an ordinary per-book failure (it records
                // the MetadataRefreshFailed issue instead), so nothing here needs a per-item
                // try/catch - the batch stops early only on the Hardcover budget exception, which
                // the result's StopReason surfaces on the completion event.
                var result = await refreshService.RefreshStaleAudiobooksAsync(dto?.OlderThanUtc, ProgressAction);

                await _organizeHub.Clients.All.MetadataRefreshComplete(
                    new MetadataRefreshComplete(
                        result.Processed, result.Total, result.Succeeded, result.Failed, result.StopReason));
            },
            () => _organizeHub.Clients.All.MetadataRefreshComplete(
                new MetadataRefreshComplete(0, 0, 0, 0, "The bulk refresh failed before it could run.")),
            _appLifetime.ApplicationStopping);
    }

    /// <summary>
    /// Refreshes only the explicitly selected books. Shaves the same <c>_bulkLock</c> and
    /// operation key as <see cref="StartBulkRefresh"/> on purpose: a selected refresh and the
    /// all-books refresh are the same operation - they hammer the same scrapers - and must be
    /// mutually exclusive. Unlike the all-books sweep, a selected book that is not refreshable is
    /// counted Failed rather than dropped, because the user explicitly picked it.
    /// </summary>
    [HttpPost("bulk-selected")]
    public IActionResult StartSelectedRefresh([FromBody] BulkSelectionDto? dto)
    {
        var error = this.ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        var audiobookIds = dto!.AudiobookIds;

        return BackgroundOperationRunner.Start(
            _bulkLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            BulkOperationKey,
            async sp =>
            {
                var refreshService = sp.GetRequiredService<IMetadataRefreshService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(BulkOperationKey, processed, total);
                    return _organizeHub.Clients.All.MetadataRefreshProgress(
                        new MetadataRefreshProgress(processed, total, succeeded, failed));
                }

                var result = await refreshService.RefreshSelectedAudiobooksAsync(audiobookIds, ProgressAction);

                await _organizeHub.Clients.All.MetadataRefreshComplete(
                    new MetadataRefreshComplete(
                        result.Processed, result.Total, result.Succeeded, result.Failed, result.StopReason));
            },
            () => _organizeHub.Clients.All.MetadataRefreshComplete(
                new MetadataRefreshComplete(0, 0, 0, 0, "The bulk refresh failed before it could run.")),
            _appLifetime.ApplicationStopping);
    }

    [HttpGet("pending")]
    public async Task<ActionResult<PendingMetadataRefreshPageDto>> GetPending(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        if (page < 0)
        {
            return this.InvalidRequest("page must be zero or greater.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return this.InvalidRequest($"pageSize must be between 1 and {MaxPageSize}.");
        }

        var skip = (long)page * pageSize;
        if (skip > MaxPageOffset)
        {
            return this.InvalidRequest($"page and pageSize together may not skip more than {MaxPageOffset} entries.");
        }

        var (items, total) = await _metadataRefreshService.GetPendingPageAsync(page, pageSize);

        return Ok(new PendingMetadataRefreshPageDto(
            items
                .Select(p => new PendingMetadataRefreshListItemDto(
                    p.AudiobookId,
                    p.Audiobook.BookName,
                    p.Audiobook.Authors.Select(a => a.Name).ToList(),
                    p.FetchedAt,
                    p.SourceName))
                .ToList(),
            total));
    }

    /// <summary>The sparse id list of books with a pending snapshot, for the library-list badges.</summary>
    [HttpGet("pending-summary")]
    public async Task<List<long>> GetPendingSummary()
    {
        return await _metadataRefreshService.GetPendingAudiobookIdsAsync();
    }

    [HttpGet("{id:long}/pending")]
    public async Task<ActionResult<PendingMetadataRefreshDto>> GetPendingForAudiobook(long id)
    {
        var pending = await _metadataRefreshService.GetPendingRefreshAsync(id);
        if (pending is null)
        {
            return NotFound();
        }

        var (row, payload) = pending.Value;
        return Ok(ToDto(payload, row.FetchedAt, row.SourceName, row.SourceUrl, id));
    }

    /// <summary>
    /// Deletes the pending snapshot for a book - after the user either applied its fields via the
    /// normal edit/save flow, or decided to discard them. Deliberately not idempotent-failing:
    /// dismissing an already-dismissed snapshot is a no-op success.
    /// </summary>
    [HttpPost("{id:long}/dismiss")]
    public async Task<IActionResult> DismissPending(long id)
    {
        await _metadataRefreshService.DismissPendingRefreshAsync(id);
        return Ok();
    }

    private static MetadataRefreshResultDto ToDto(MetadataRefreshResult result) =>
        new(
            result.Success,
            result.HasDifferences,
            result.Differences.Select(d => new MetadataRefreshDiffDto(d.Field, d.LibraryValue, d.SourceValue)).ToList(),
            result.SourceName,
            result.Error);

    private static PendingMetadataRefreshDto ToDto(
        PendingRefreshPayload.Snapshot payload,
        DateTime fetchedAt,
        string sourceName,
        string sourceUrl,
        long audiobookId) =>
        new(
            audiobookId,
            fetchedAt,
            sourceName,
            sourceUrl,
            new PendingRefreshSnapshotDto(
                payload.Url,
                payload.Source,
                payload.Authors.ToList(),
                payload.Narrators.ToList(),
                payload.BookName,
                payload.Subtitle,
                payload.SeriesName,
                payload.SeriesPart,
                payload.Year,
                payload.Genres.ToList(),
                payload.Description,
                payload.Language,
                payload.Rating,
                payload.Copyright,
                payload.Publisher,
                payload.Asin));
}