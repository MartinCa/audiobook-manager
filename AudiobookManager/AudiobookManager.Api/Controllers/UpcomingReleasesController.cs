using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

/// <summary>
/// The consolidated upcoming-releases surface: every release discovered for a followed author or
/// series, optionally scoped to one of either (the author/series detail pages use the scoped
/// form; the dedicated releases page uses the unscoped one). Following/matching authors lives on
/// <see cref="BrowseController"/> alongside the rest of the author endpoints; following a series
/// lives on <see cref="SeriesController"/> alongside its existing match/refresh flow.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class UpcomingReleasesController : ControllerBase
{
    /// <summary>
    /// Shared with <see cref="Workers.UpcomingReleasesWorker"/>'s periodic sweep, so a manual
    /// refresh and a scheduled tick can never run concurrently against Hardcover - both call the
    /// exact same <see cref="IUpcomingReleaseService.RefreshUpcomingReleasesAsync"/> sweep, and
    /// running two at once would double the request spend for the same discoveries.
    /// </summary>
    internal static readonly SemaphoreSlim RefreshGate = new(1, 1);

    public const string RefreshOperationKey = "upcoming-releases-refresh";

    private readonly IUpcomingReleaseService _upcomingReleaseService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<UpcomingReleasesController> _logger;

    public UpcomingReleasesController(
        IUpcomingReleaseService upcomingReleaseService,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IHostApplicationLifetime appLifetime,
        ILogger<UpcomingReleasesController> logger)
    {
        _upcomingReleaseService = upcomingReleaseService;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<UpcomingReleaseDto>>> GetUpcomingReleases(
        [FromQuery] long? authorId = null,
        [FromQuery] long? seriesId = null,
        [FromQuery] int limit = PagingLimits.DefaultPageSize,
        [FromQuery] int offset = 0)
    {
        if (limit < 1 || limit > PagingLimits.MaxPageSize)
        {
            return this.InvalidRequest($"limit must be between 1 and {PagingLimits.MaxPageSize}.");
        }

        if (offset < 0 || offset > PagingLimits.MaxPageOffset)
        {
            return this.InvalidRequest($"offset must be between 0 and {PagingLimits.MaxPageOffset}.");
        }

        try
        {
            var (items, total) = await _upcomingReleaseService.GetUpcomingReleasesAsync(authorId, seriesId, limit, offset);
            var dtos = items.Select(ToDto).ToList();
            return new PaginatedResult<UpcomingReleaseDto>(dtos.Count, total, dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching upcoming releases (authorId {AuthorId}, seriesId {SeriesId})", authorId, seriesId);
            return this.UnexpectedError();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveUpcomingRelease(long id)
    {
        try
        {
            var deleted = await _upcomingReleaseService.RemoveUpcomingReleaseAsync(id);
            return deleted ? Ok() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing upcoming release {Id}", id);
            return this.UnexpectedError();
        }
    }

    /// <summary>
    /// Fire-and-forget: polls every followed-and-matched author/series right now rather than
    /// waiting for the periodic worker's next tick - the "refresh now" affordance
    /// <c>SeriesController.RefreshAllSeries</c> gives the whole series catalog (not the
    /// synchronous single-series <c>RefreshSeries</c>, since this sweeps every followed
    /// author/series and can run for minutes at the Hardcover rate limit's pace). Progress is not
    /// reported over SignalR - like <c>MissingTagsController.StartLanguageBackfill</c>, the client
    /// follows it by polling <c>GET api/operations/{key}/status</c>. <see cref="RefreshGate"/> is
    /// shared with <see cref="Workers.UpcomingReleasesWorker"/>, so a busy gate here means a
    /// scheduled tick is already running - a 409, not a stacked duplicate sweep.
    /// </summary>
    [HttpPost("refresh")]
    public IActionResult RefreshUpcomingReleases()
    {
        return BackgroundOperationRunner.Start(
            RefreshGate,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            RefreshOperationKey,
            async sp =>
            {
                var upcomingReleaseService = sp.GetRequiredService<IUpcomingReleaseService>();
                await upcomingReleaseService.RefreshUpcomingReleasesAsync();
            },
            () => Task.CompletedTask,
            _appLifetime.ApplicationStopping);
    }

    private static UpcomingReleaseDto ToDto(UpcomingRelease r) => new(
        r.Id,
        r.Title,
        r.ReleaseDate,
        r.PersonId,
        r.Person?.Name,
        r.SeriesId,
        r.Series?.Name,
        r.SeriesPosition,
        r.SourceName,
        r.SourceUrl,
        r.ImageUrl);
}
