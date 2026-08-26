using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

/// <summary>
/// Series are addressed by their free-text name rather than a catalog id: a series that has
/// never been matched exists only as a value on audiobooks and has no catalog row (and so no
/// id) yet, but still needs to be browsable and matchable.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class SeriesController : ControllerBase
{
    private static readonly SemaphoreSlim _matchLock = new(1, 1);
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ISeriesService _seriesService;
    private readonly ILogger<SeriesController> _logger;

    public SeriesController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        ISeriesService seriesService,
        ILogger<SeriesController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _seriesService = seriesService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<List<SeriesOverviewDto>> GetAllSeries()
    {
        var overviews = await _seriesService.GetAllSeriesOverviewAsync();
        return overviews.Select(ToDto).ToList();
    }

    [HttpGet("{seriesName}")]
    public async Task<ActionResult<SeriesDetailDto>> GetSeriesDetail(string seriesName)
    {
        var detail = await _seriesService.GetSeriesDetailAsync(seriesName);
        if (detail is null)
        {
            return NotFound();
        }

        return new SeriesDetailDto(
            ToDto(detail.Overview),
            detail.OwnedBooks.Select(b => new SeriesOwnedBookDto(
                b.Id, b.BookName, b.SeriesPart, b.Year, b.Authors, b.Narrators, b.DurationInSeconds)).ToList(),
            detail.MissingBooks.Select(ToDto).ToList(),
            detail.IgnoredBooks.Select(ToDto).ToList());
    }

    [HttpGet("{seriesName}/match-candidates")]
    public async Task<ActionResult<List<SeriesMatchCandidateDto>>> GetMatchCandidates(string seriesName)
    {
        try
        {
            var candidates = await _seriesService.SuggestSeriesMatchesAsync(seriesName);
            return candidates.Select(c => new SeriesMatchCandidateDto(
                c.SourceName, c.SourceId, c.SeriesName, c.SourceUrl, c.Authors, c.BookCount, c.Confidence)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching match candidates for series {SeriesName}", seriesName);
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{seriesName}/match")]
    public async Task<ActionResult<SeriesOverviewDto>> MatchSeries(string seriesName, [FromBody] MatchSeriesDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.SourceName) || string.IsNullOrWhiteSpace(dto.SourceId))
        {
            return BadRequest("SourceName and SourceId are required");
        }

        try
        {
            var overview = await _seriesService.MatchSeriesAsync(seriesName, dto.SourceName, dto.SourceId, dto.Confidence);
            return ToDto(overview);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error matching series {SeriesName} to {SourceName}/{SourceId}", seriesName, dto.SourceName, dto.SourceId);
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("match/bulk")]
    public IActionResult StartBulkMatch([FromBody] BulkMatchSeriesDto dto)
    {
        var threshold = dto?.ConfidenceThreshold ?? 0.85;
        if (threshold is < 0 or > 1)
        {
            return BadRequest("ConfidenceThreshold must be between 0 and 1");
        }

        var seriesNames = dto?.SeriesNames is { Count: > 0 } ? dto.SeriesNames : null;

        return BackgroundOperationRunner.Start(
            _matchLock,
            _serviceScopeFactory,
            _logger,
            async sp =>
            {
                var seriesService = sp.GetRequiredService<ISeriesService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed) =>
                    _organizeHub.Clients.All.SeriesMatchProgress(
                        new SeriesMatchProgress(processed, total, succeeded, failed));

                var (processed, succeeded, failed) =
                    await seriesService.BulkAutoMatchSeriesAsync(threshold, seriesNames, ProgressAction);

                await _organizeHub.Clients.All.SeriesMatchComplete(
                    new SeriesMatchComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.SeriesMatchComplete(new SeriesMatchComplete(0, 0, 0)));
    }

    [HttpPost("{seriesName}/refresh")]
    public IActionResult StartRefreshSeries(string seriesName)
    {
        return StartRefresh(service => service.RefreshSeriesAsync(seriesName, RefreshProgressAction));
    }

    [HttpPost("refresh-all")]
    public IActionResult StartRefreshAllSeries()
    {
        return StartRefresh(service => service.RefreshAllSeriesAsync(RefreshProgressAction));
    }

    [HttpPost("expected-books/{id}/ignore")]
    public Task<IActionResult> IgnoreExpectedBook(long id) => SetIgnored(id, true);

    [HttpDelete("expected-books/{id}/ignore")]
    public Task<IActionResult> UnignoreExpectedBook(long id) => SetIgnored(id, false);

    private async Task<IActionResult> SetIgnored(long id, bool ignored)
    {
        try
        {
            await _seriesService.IgnoreExpectedBookAsync(id, ignored);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting ignored={Ignored} on expected book {ExpectedBookId}", ignored, id);
            return StatusCode(500, ex.Message);
        }
    }

    private Task RefreshProgressAction(int processed, int total, int succeeded, int failed) =>
        _organizeHub.Clients.All.SeriesRefreshProgress(
            new SeriesRefreshProgress(processed, total, succeeded, failed));

    private IActionResult StartRefresh(Func<ISeriesService, Task<(int Processed, int Succeeded, int Failed)>> work)
    {
        return BackgroundOperationRunner.Start(
            _refreshLock,
            _serviceScopeFactory,
            _logger,
            async sp =>
            {
                var seriesService = sp.GetRequiredService<ISeriesService>();
                var (processed, succeeded, failed) = await work(seriesService);

                await _organizeHub.Clients.All.SeriesRefreshComplete(
                    new SeriesRefreshComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.SeriesRefreshComplete(new SeriesRefreshComplete(0, 0, 0)));
    }

    private static SeriesOverviewDto ToDto(SeriesOverview o) => new(
        o.Id,
        o.Name,
        o.Authors,
        o.OwnedBookCount,
        o.IsMatched,
        o.MatchedSourceName,
        o.MatchedSourceId,
        o.MatchedSourceUrl,
        o.MatchConfidence,
        o.LastRefreshedAt,
        o.ExpectedBookCount,
        o.MissingBookCount,
        o.IgnoredBookCount);

    private static SeriesExpectedBookDto ToDto(SeriesExpectedBookInfo b) => new(
        b.Id, b.Title, b.Position, b.Year, b.SourceUrl, b.IsIgnored);
}
