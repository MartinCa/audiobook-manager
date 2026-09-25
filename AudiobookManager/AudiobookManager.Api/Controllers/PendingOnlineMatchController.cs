using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PendingOnlineMatchController : ControllerBase
{
    public const string SearchOperationKey = "pending-online-match-search";

    private static readonly SemaphoreSlim _searchLock = new(1, 1);

    private readonly IPendingOnlineMatchService _service;
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<PendingOnlineMatchController> _logger;

    public PendingOnlineMatchController(
        IPendingOnlineMatchService service,
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IHostApplicationLifetime appLifetime,
        ILogger<PendingOnlineMatchController> logger)
    {
        _service = service;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    /// <summary>
    /// Searches every explicitly selected book across the given sources. Fire-and-forget through
    /// <see cref="BackgroundOperationRunner"/> with SignalR progress, the same shape as the
    /// metadata-refresh bulk endpoints.
    /// </summary>
    [HttpPost("search-selected")]
    public IActionResult StartSearchSelected([FromBody] BulkOnlineMatchSearchDto? dto)
    {
        var error = this.ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        if (dto?.SourceNames is null || dto.SourceNames.Count == 0)
        {
            return this.InvalidRequest("At least one metadata source must be selected.");
        }

        var audiobookIds = dto.AudiobookIds;
        var sourceNames = dto.SourceNames;

        return BackgroundOperationRunner.Start(
            _searchLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            SearchOperationKey,
            async sp =>
            {
                var service = sp.GetRequiredService<IPendingOnlineMatchService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(SearchOperationKey, processed, total);
                    return _organizeHub.Clients.All.PendingOnlineMatchSearchProgress(
                        new PendingOnlineMatchSearchProgress(processed, total, succeeded, failed));
                }

                var result = await service.SearchSelectedAsync(audiobookIds, sourceNames, ProgressAction);

                await _organizeHub.Clients.All.PendingOnlineMatchSearchComplete(
                    new PendingOnlineMatchSearchComplete(
                        result.Processed, result.Total, result.Succeeded, result.Failed));
            },
            () => _organizeHub.Clients.All.PendingOnlineMatchSearchComplete(
                new PendingOnlineMatchSearchComplete(0, 0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    [HttpGet("pending")]
    public async Task<ActionResult<PendingOnlineMatchPageDto>> GetPending(
        [FromQuery] int page = 0, [FromQuery] int pageSize = PagingLimits.DefaultPageSize) =>
        await GetPage(PendingOnlineMatchStatus.Pending, page, pageSize);

    [HttpGet("failed")]
    public async Task<ActionResult<PendingOnlineMatchPageDto>> GetFailed(
        [FromQuery] int page = 0, [FromQuery] int pageSize = PagingLimits.DefaultPageSize) =>
        await GetPage(PendingOnlineMatchStatus.Rejected, page, pageSize);

    private async Task<ActionResult<PendingOnlineMatchPageDto>> GetPage(
        PendingOnlineMatchStatus status, int page, int pageSize)
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

        var (items, total) = await _service.GetPageAsync(status, page, pageSize);

        return Ok(new PendingOnlineMatchPageDto(
            items.Select(ToListItemDto).ToList(),
            total));
    }

    /// <summary>
    /// Fetches full details for the chosen candidate and hands them to the normal pending
    /// metadata-refresh review/apply flow - the book's row here is then resolved (deleted).
    /// Synchronous: one book, one extra HTTP fetch, the same latency profile
    /// metadata-search/details already has.
    /// </summary>
    [HttpPost("{id:long}/select")]
    public async Task<IActionResult> SelectResult(long id, [FromBody] SelectOnlineMatchResultDto? dto)
    {
        if (dto is null)
        {
            return this.InvalidRequest("resultIndex is required.");
        }

        try
        {
            await _service.SelectResultAsync(id, dto.ResultIndex);
            return Ok();
        }
        catch (KeyNotFoundException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Scraping.RateLimiting.HardcoverDailyLimitExceededException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
    }

    [HttpPost("{id:long}/reject")]
    public async Task<IActionResult> Reject(long id)
    {
        await _service.RejectAsync(id);
        return Ok();
    }

    /// <summary>Dismisses a book from the Failed/Rejected list. Not idempotent-failing: dismissing an already-gone row is a no-op success.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Dismiss(long id)
    {
        await _service.DismissAsync(id);
        return Ok();
    }

    private static PendingOnlineMatchListItemDto ToListItemDto(PendingOnlineMatch row) => new(
        row.AudiobookId,
        row.Audiobook.BookName,
        row.Audiobook.Authors.Select(a => a.Name).ToList(),
        row.SearchedAt,
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(row.SourceNamesJson) ?? new List<string>(),
        PendingOnlineMatchPayload.Parse(row.ResultsJson)
            .Select(s => new PendingRefreshSnapshotDto(
                s.Url,
                s.Source,
                s.Authors.ToList(),
                s.Narrators.ToList(),
                s.BookName,
                s.Subtitle,
                s.SeriesName,
                s.SeriesPart,
                s.Year,
                s.Genres.ToList(),
                s.Description,
                s.Language,
                s.Rating,
                s.Copyright,
                s.Publisher,
                s.Asin))
            .ToList());
}
