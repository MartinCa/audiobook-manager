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
    public const string BulkOperationKey = "metadata-refresh";
    public const string ApplyOperationKey = "metadata-apply";

    private static readonly SemaphoreSlim _bulkLock = new(1, 1);
    private static readonly SemaphoreSlim _applyLock = new(1, 1);

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
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize,
        [FromQuery] List<string>? fields = null,
        [FromQuery] List<string>? sources = null)
    {
        if (page < 0)
        {
            return this.InvalidRequest("page must be zero or greater.");
        }

        if (pageSize < 1 || pageSize > PagingLimits.MaxPageSize)
        {
            return this.InvalidRequest($"pageSize must be between 1 and {PagingLimits.MaxPageSize}.");
        }

        var skip = (long)page * pageSize;
        if (skip > PagingLimits.MaxPageOffset)
        {
            return this.InvalidRequest($"page and pageSize together may not skip more than {PagingLimits.MaxPageOffset} entries.");
        }

        var (items, total) = await _metadataRefreshService.GetPendingPageAsync(page, pageSize, fields, sources);

        return Ok(new PendingMetadataRefreshPageDto(
            items
                .Select(p => new PendingMetadataRefreshListItemDto(
                    p.AudiobookId,
                    p.Audiobook.BookName,
                    p.Audiobook.Authors.Select(a => a.Name).ToList(),
                    p.FetchedAt,
                    p.SourceName,
                    MetadataRefreshFields.ParseChangedFieldsJson(p.ChangedFieldsJson)))
                .ToList(),
            total));
    }

    /// <summary>
    /// The sparse id list of books with a pending snapshot, for the library-list badges when
    /// <paramref name="fields"/> and <paramref name="sources"/> are both omitted, or every id
    /// matching those filters - the full match set (not one page) that "apply every book matching
    /// this filter" resolves ids through, so the client never has to walk every page to find them
    /// all.
    /// </summary>
    [HttpGet("pending-summary")]
    public async Task<List<long>> GetPendingSummary(
        [FromQuery] List<string>? fields = null, [FromQuery] List<string>? sources = null)
    {
        return await _metadataRefreshService.GetPendingAudiobookIdsAsync(fields, sources);
    }

    /// <summary>
    /// Applies one book's pending snapshot immediately - the metadata-refresh page's per-row
    /// quick apply. Synchronous (one book, one save) unlike the bulk endpoints below.
    /// </summary>
    [HttpPost("{id:long}/apply")]
    public async Task<IActionResult> ApplyPending(long id, [FromBody] ApplyPendingRefreshDto? dto)
    {
        try
        {
            var applied = await _metadataRefreshService.ApplyPendingRefreshAsync(id, dto?.Fields);
            return applied ? Ok() : NoContent();
        }
        catch (AudiobookBusyException ex)
        {
            // The apply takes the same per-audiobook save gate an interactive save/resolve/align
            // does (MetadataRefreshService.ApplyOneAsync), so another operation already holding
            // it for this book is a "try again", not a server error.
            return this.ConflictingState(ex.Message, "Cannot apply pending refresh");
        }
        catch (InvalidOperationException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
    }

    /// <summary>
    /// Applies every explicitly selected book's full pending snapshot. Fire-and-forget through
    /// <see cref="BackgroundOperationRunner"/> with SignalR progress, the same shape as the
    /// bulk-edit/bulk-refresh endpoints; capped by <see cref="BulkSelectionValidation.MaxSelection"/>
    /// like every other explicit-selection endpoint.
    /// </summary>
    [HttpPost("apply-selected")]
    public IActionResult StartApplySelected([FromBody] BulkSelectionDto? dto)
    {
        var error = this.ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        var audiobookIds = dto!.AudiobookIds;

        return BackgroundOperationRunner.Start(
            _applyLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ApplyOperationKey,
            async sp =>
            {
                var refreshService = sp.GetRequiredService<IMetadataRefreshService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(ApplyOperationKey, processed, total);
                    return _organizeHub.Clients.All.MetadataApplyProgress(
                        new MetadataApplyProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed) =
                    await refreshService.ApplySelectedPendingRefreshesAsync(audiobookIds, ProgressAction);

                await _organizeHub.Clients.All.MetadataApplyComplete(
                    new MetadataApplyComplete(processed, audiobookIds.Count, succeeded, failed));
            },
            () => _organizeHub.Clients.All.MetadataApplyComplete(new MetadataApplyComplete(0, 0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    /// <summary>
    /// Applies every pending book whose stored changed-fields are entirely contained in the
    /// given field list - "apply every book matching this filter", unbounded by page or the
    /// explicit-selection cap, resolved and run entirely server-side. Same fire-and-forget shape
    /// as <see cref="StartApplySelected"/>, sharing its lock: the two must not run concurrently,
    /// since either can touch the same book.
    /// </summary>
    [HttpPost("apply-filtered")]
    public IActionResult StartApplyFiltered([FromBody] BulkApplyFilteredMetadataRefreshDto? dto)
    {
        if (dto?.Fields is null || dto.Fields.Count == 0)
        {
            return this.InvalidRequest("At least one field must be selected.");
        }

        var fields = dto.Fields;

        return BackgroundOperationRunner.Start(
            _applyLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ApplyOperationKey,
            async sp =>
            {
                var refreshService = sp.GetRequiredService<IMetadataRefreshService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(ApplyOperationKey, processed, total);
                    return _organizeHub.Clients.All.MetadataApplyProgress(
                        new MetadataApplyProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed) =
                    await refreshService.ApplyFilteredPendingRefreshesAsync(fields, ProgressAction);

                await _organizeHub.Clients.All.MetadataApplyComplete(
                    new MetadataApplyComplete(processed, processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.MetadataApplyComplete(new MetadataApplyComplete(0, 0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    /// <summary>
    /// Re-evaluates every pending snapshot against the library, series mapping patterns, and
    /// changed-fields logic as they stand right now, without re-scraping anything - so a mapping
    /// pattern added after a snapshot was captured (or any other setting/book change since) is
    /// reflected without waiting for the book's next scheduled refresh. Synchronous: pure DB/CPU
    /// work, no scraper calls, sized the same way the self-heal backfill is.
    /// </summary>
    [HttpPost("reevaluate")]
    public async Task<ActionResult<MetadataRefreshReevaluateResultDto>> ReevaluatePending()
    {
        var result = await _metadataRefreshService.ReevaluatePendingRefreshesAsync();
        var dto = new MetadataRefreshReevaluateResultDto(result.Processed, result.Updated, result.Removed);
        return Ok(dto);
    }

    [HttpGet("{id:long}/pending")]
    public async Task<ActionResult<PendingMetadataRefreshDto>> GetPendingForAudiobook(long id)
    {
        var pending = await _metadataRefreshService.GetPendingRefreshAsync(id);
        if (pending is null)
        {
            // 204, not 404: "this book has no pending snapshot" is a normal state that the book
            // page polls on every visit, and every non-2xx response is logged by the browser as
            // a failed request that no client-side handling can silence. No body needed - the
            // status alone says the snapshot is absent.
            return NoContent();
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

    /// <summary>
    /// Deletes every explicitly selected book's pending snapshot without applying it - the bulk
    /// counterpart of <see cref="DismissPending"/>. A pure DB delete, so unlike the apply/refresh
    /// bulk endpoints this runs synchronously and returns the count deleted, needing no
    /// <see cref="BackgroundOperationRunner"/>/SignalR progress.
    /// </summary>
    [HttpPost("dismiss-selected")]
    public async Task<ActionResult<DismissSelectedMetadataRefreshResultDto>> DismissSelected([FromBody] BulkSelectionDto? dto)
    {
        var error = this.ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        var dismissed = await _metadataRefreshService.DismissSelectedPendingRefreshesAsync(dto!.AudiobookIds);
        return Ok(new DismissSelectedMetadataRefreshResultDto(dismissed));
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