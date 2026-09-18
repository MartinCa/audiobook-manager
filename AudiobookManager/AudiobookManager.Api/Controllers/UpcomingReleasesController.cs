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
    private readonly IUpcomingReleaseService _upcomingReleaseService;
    private readonly ILogger<UpcomingReleasesController> _logger;

    public UpcomingReleasesController(
        IUpcomingReleaseService upcomingReleaseService,
        ILogger<UpcomingReleasesController> logger)
    {
        _upcomingReleaseService = upcomingReleaseService;
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
    /// Polls every followed-and-matched author/series right now, synchronously, rather than
    /// waiting for the periodic worker's next tick - the same "refresh now" affordance
    /// <c>SeriesController.RefreshSeries</c> gives a single series. A single author/series
    /// failure does not fail the request; only an unhandled failure in the sweep itself does.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshUpcomingReleases()
    {
        try
        {
            await _upcomingReleaseService.RefreshUpcomingReleasesAsync();
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing upcoming releases");
            return this.UnexpectedError();
        }
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
