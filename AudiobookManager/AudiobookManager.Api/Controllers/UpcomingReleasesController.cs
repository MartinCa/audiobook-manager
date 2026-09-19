using System.Diagnostics;
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
    /// Dismisses a roster-derived ("Source": "Roster") item from the unified list - there is no
    /// backing <c>UpcomingRelease</c> row to <c>DELETE</c>, so this sets <c>IsIgnored</c> on the
    /// matching series/author roster entry instead, exactly like the series/author detail pages'
    /// own missing-books ignore action.
    /// </summary>
    [HttpPost("dismiss-roster")]
    public async Task<IActionResult> DismissRosterUpcomingRelease([FromBody] DismissRosterUpcomingReleaseDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
        {
            return this.InvalidRequest("Title is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.SeriesName) == (dto.AuthorId is null))
        {
            return this.InvalidRequest("Exactly one of seriesName or authorId must be set.");
        }

        try
        {
            if (dto.AuthorId is not null)
            {
                await _upcomingReleaseService.DismissAuthorRosterUpcomingAsync(dto.AuthorId.Value, dto.Title);
            }
            else
            {
                await _upcomingReleaseService.DismissSeriesRosterUpcomingAsync(dto.SeriesName!, dto.SeriesPosition, dto.Title);
            }

            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dismissing roster-derived upcoming release (title {Title})", dto.Title);
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
                // Shares UpcomingReleasesController.RefreshGate/RefreshOperationKey with
                // UpcomingReleasesWorker's scheduled tick, so both write into the same
                // scheduled_task_runs row - a user who always triggers refreshes manually should
                // still see an accurate "last run" on the Settings Tasks page, not "never run".
                var startedAt = DateTime.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                string? error = null;
                try
                {
                    var upcomingReleaseService = sp.GetRequiredService<IUpcomingReleaseService>();
                    await upcomingReleaseService.RefreshUpcomingReleasesAsync();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    var scheduledTaskService = sp.GetRequiredService<IScheduledTaskService>();
                    await scheduledTaskService.RecordTaskRunAsync(
                        ScheduledTaskKeys.UpcomingReleasesRefresh, startedAt, stopwatch.Elapsed, error is null, error);
                }
            },
            () => Task.CompletedTask,
            _appLifetime.ApplicationStopping);
    }

    private static UpcomingReleaseDto ToDto(UpcomingReleaseItem i) => new(
        i.Source.ToString(),
        i.Id,
        i.Title,
        i.ReleaseDate,
        i.Year,
        i.AuthorId,
        i.AuthorName,
        i.SeriesId,
        i.SeriesName,
        i.SeriesPosition,
        i.SourceName,
        i.SourceUrl,
        i.ImageUrl);
}
