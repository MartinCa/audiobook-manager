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
///
/// That name travels in the query string, never as a path segment. A series value is a raw m4b
/// tag, so it can contain any character - and a name with a "/" in it ("Sword Art Online /
/// Progressive") is unaddressable in a path: ASP.NET Core leaves %2F encoded rather than
/// decoding it into a segment separator, so the action received the literal "%2F" and every
/// lookup missed. The series was listed on the overview page and then 404'd the moment it was
/// opened, with no way to match, refresh or ignore anything in it.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class SeriesController : ControllerBase
{
    private static readonly SemaphoreSlim _matchLock = new(1, 1);
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>The largest page a caller may ask for. Beyond this the response stops being a page.</summary>
    private const int MaxPageSize = 200;

    private const int DefaultPageSize = 50;

    /// <summary>
    /// The furthest into a paged list a caller may ask to start. See UrlCleanupController's
    /// MaxPageOffset for the two reasons it is bounded.
    /// </summary>
    private const long MaxPageOffset = 1_000_000;

    public const string MatchOperationKey = "series-match";
    public const string RefreshOperationKey = "series-refresh";

    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly ISeriesService _seriesService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ILibraryConsistencyService _libraryConsistencyService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<SeriesController> _logger;

    public SeriesController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        ISeriesService seriesService,
        IAudiobookSaveGate saveGate,
        ILibraryConsistencyService libraryConsistencyService,
        IHostApplicationLifetime appLifetime,
        ILogger<SeriesController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _seriesService = seriesService;
        _saveGate = saveGate;
        _libraryConsistencyService = libraryConsistencyService;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<SeriesOverviewPageDto>> GetSeries(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] bool? matched = null)
    {
        var pagingError = ValidatePageSelection(page, pageSize, "series");
        if (pagingError != null)
        {
            return pagingError;
        }

        var overviewPage = await _seriesService.GetSeriesOverviewPageAsync(page, pageSize, search, matched);
        return Ok(new SeriesOverviewPageDto(overviewPage.Items.Select(ToDto).ToList(), overviewPage.TotalCount));
    }

    [HttpGet("counts")]
    public async Task<SeriesCountsDto> GetSeriesCounts()
    {
        var counts = await _seriesService.GetSeriesOverviewCountsAsync();
        return new SeriesCountsDto(counts.Total, counts.Matched, counts.Unmatched);
    }

    [HttpGet("detail")]
    public async Task<ActionResult<SeriesDetailDto>> GetSeriesDetail(
        [FromQuery] string seriesName,
        [FromQuery] int ownedPage = 0,
        [FromQuery] int ownedPageSize = DefaultPageSize,
        [FromQuery] int missingPage = 0,
        [FromQuery] int missingPageSize = DefaultPageSize,
        [FromQuery] int ignoredPage = 0,
        [FromQuery] int ignoredPageSize = DefaultPageSize)
    {
        foreach (var check in new[]
        {
            ValidatePageSelection(ownedPage, ownedPageSize, "owned books"),
            ValidatePageSelection(missingPage, missingPageSize, "missing books"),
            ValidatePageSelection(ignoredPage, ignoredPageSize, "ignored books"),
        })
        {
            if (check != null)
            {
                return check;
            }
        }

        var detail = await _seriesService.GetSeriesDetailPageAsync(
            seriesName,
            ownedSkip: (int)((long)ownedPage * ownedPageSize), ownedTake: ownedPageSize,
            missingSkip: (int)((long)missingPage * missingPageSize), missingTake: missingPageSize,
            ignoredSkip: (int)((long)ignoredPage * ignoredPageSize), ignoredTake: ignoredPageSize);
        if (detail is null)
        {
            return NotFound();
        }

        return new SeriesDetailDto(
            ToDto(detail.Overview),
            new SeriesOwnedBookPageDto(detail.OwnedBooks.Select(b => new SeriesOwnedBookDto(
                b.Id, b.BookName, b.SeriesPart, b.Year, b.Authors, b.Narrators, b.DurationInSeconds)).ToList(), detail.OwnedBookTotal),
            new SeriesExpectedBookPageDto(detail.MissingBooks.Select(ToDto).ToList(), detail.MissingBookTotal),
            new SeriesExpectedBookPageDto(detail.IgnoredBooks.Select(ToDto).ToList(), detail.IgnoredBookTotal));
    }

    [HttpGet("match-candidates")]
    public async Task<ActionResult<List<SeriesMatchCandidateDto>>> GetMatchCandidates([FromQuery] string seriesName)
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
            return this.UnexpectedError();
        }
    }

    [HttpGet("match-candidates/search")]
    public async Task<ActionResult<List<SeriesMatchCandidateDto>>> SearchMatchCandidates([FromQuery] string seriesName, [FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return this.InvalidRequest("Query is required.");
        }

        try
        {
            var candidates = await _seriesService.SearchSeriesMatchesAsync(seriesName, query);
            return candidates.Select(c => new SeriesMatchCandidateDto(
                c.SourceName, c.SourceId, c.SeriesName, c.SourceUrl, c.Authors, c.BookCount, c.Confidence)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching match candidates for series {SeriesName} with query {Query}", seriesName, query);
            return this.UnexpectedError();
        }
    }

    [HttpPost("match")]
    public async Task<ActionResult<SeriesOverviewDto>> MatchSeries([FromQuery] string seriesName, [FromBody] MatchSeriesDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.SourceName) || string.IsNullOrWhiteSpace(dto.SourceId))
        {
            return this.InvalidRequest("SourceName and SourceId are required.");
        }

        try
        {
            var overview = await _seriesService.MatchSeriesAsync(seriesName, dto.SourceName, dto.SourceId, dto.Confidence, dto.IncludeOmnibusEditions);
            return ToDto(overview);
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error matching series {SeriesName} to {SourceName}/{SourceId}", seriesName, dto.SourceName, dto.SourceId);
            return this.UnexpectedError();
        }
    }

    [HttpPost("include-omnibus-editions")]
    public async Task<ActionResult<SeriesOverviewDto>> SetIncludeOmnibusEditions([FromQuery] string seriesName, [FromBody] IncludeOmnibusEditionsDto dto)
    {
        try
        {
            var overview = await _seriesService.SetIncludeOmnibusEditionsAsync(seriesName, dto.IncludeOmnibusEditions);
            return ToDto(overview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting IncludeOmnibusEditions={Include} for series {SeriesName}", dto.IncludeOmnibusEditions, seriesName);
            return this.UnexpectedError();
        }
    }

    [HttpPost("match/bulk")]
    public IActionResult StartBulkMatch([FromBody] BulkMatchSeriesDto dto)
    {
        var threshold = dto?.ConfidenceThreshold ?? 0.85;
        if (threshold is < 0 or > 1)
        {
            return this.InvalidRequest("ConfidenceThreshold must be between 0 and 1.");
        }

        var seriesNames = dto?.SeriesNames is { Count: > 0 } ? dto.SeriesNames : null;

        return BackgroundOperationRunner.Start(
            _matchLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            MatchOperationKey,
            async sp =>
            {
                var seriesService = sp.GetRequiredService<ISeriesService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(MatchOperationKey, processed, total);
                    return _organizeHub.Clients.All.SeriesMatchProgress(
                        new SeriesMatchProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed, stopReason) =
                    await seriesService.BulkAutoMatchSeriesAsync(threshold, seriesNames, ProgressAction);

                await _organizeHub.Clients.All.SeriesMatchComplete(
                    new SeriesMatchComplete(processed, succeeded, failed, stopReason));
            },
            () => _organizeHub.Clients.All.SeriesMatchComplete(new SeriesMatchComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    [HttpPost("refresh")]
    public IActionResult StartRefreshSeries([FromQuery] string seriesName)
    {
        return StartRefresh(service => service.RefreshSeriesAsync(seriesName, RefreshProgressAction));
    }

    [HttpPost("refresh-all")]
    public IActionResult StartRefreshAllSeries()
    {
        return StartRefresh(service => service.RefreshAllSeriesAsync(RefreshProgressAction));
    }

    [HttpGet("expected-books/candidates")]
    public async Task<ActionResult<List<SeriesBookCandidateDto>>> GetMissingBookCandidates(
        [FromQuery] string seriesName,
        [FromQuery] string? position,
        [FromQuery] string? title)
    {
        if (string.IsNullOrWhiteSpace(position) && string.IsNullOrWhiteSpace(title))
        {
            return this.InvalidRequest("Position or Title is required to identify the expected book.");
        }

        try
        {
            var candidates = await _seriesService.FindMissingBookCandidatesAsync(seriesName, position, title);
            return candidates.Select(c => new SeriesBookCandidateDto(
                c.AudiobookId, c.BookName, c.Series, c.SeriesPart, c.Year, c.Authors, c.TitleSimilarity, c.AuthorMatches)).ToList();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding library candidates for expected book (position {Position}, title {Title}) of series {SeriesName}",
                position, title, seriesName);
            return this.UnexpectedError();
        }
    }

    [HttpPost("expected-books/apply")]
    public async Task<IActionResult> ApplyExpectedBook([FromQuery] string seriesName, [FromBody] ApplyExpectedBookDto? dto)
    {
        if (dto is null || dto.AudiobookId <= 0)
        {
            return this.InvalidRequest("A valid AudiobookId is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.Position) && string.IsNullOrWhiteSpace(dto.Title))
        {
            return this.InvalidRequest("Position or Title is required to identify the expected book.");
        }

        // Taken here rather than inside the service so the 409 - and the save-status endpoint that
        // reads the same gate - are exact from the moment this action returns, exactly like the
        // save PUT in AudiobookController. The lease is held across the apply and the follow-up
        // recheck, and released in the finally below.
        if (!_saveGate.TryAcquire(dto.AudiobookId, out var lease))
        {
            return this.ConflictingState($"A save for audiobook {dto.AudiobookId} is already in progress.", "Save in progress");
        }

        try
        {
            await _seriesService.ApplyMissingBookAsync(seriesName, dto.Position, dto.Title, dto.AudiobookId);

            // Same tail as the save PUT: the assignment rewrote tags and possibly moved the file,
            // so stored issues for this book are stale until rechecked. A recheck failure must not
            // fail the request - the assignment itself succeeded.
            try
            {
                await _libraryConsistencyService.RecheckAudiobookAsync(dto.AudiobookId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after applying a series assignment", dto.AudiobookId);
            }

            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying expected book (position {Position}, title {Title}) of series {SeriesName} to audiobook {AudiobookId}",
                dto.Position, dto.Title, seriesName, dto.AudiobookId);
            return this.UnexpectedError();
        }
        finally
        {
            lease.Dispose();
        }
    }

    // Roster entries are addressed by their natural key (series name plus position and/or
    // title), not by row id: matching and refreshing delete and re-insert the whole roster,
    // so an id a client cached earlier can point at a different book by the time it is used.
    [HttpPost("expected-books/ignore")]
    public Task<IActionResult> IgnoreExpectedBook([FromQuery] string seriesName, [FromBody] ExpectedBookRefDto dto) =>
        SetIgnored(seriesName, dto, true);

    [HttpPost("expected-books/unignore")]
    public Task<IActionResult> UnignoreExpectedBook([FromQuery] string seriesName, [FromBody] ExpectedBookRefDto dto) =>
        SetIgnored(seriesName, dto, false);

    private async Task<IActionResult> SetIgnored(string seriesName, ExpectedBookRefDto? dto, bool ignored)
    {
        if (string.IsNullOrWhiteSpace(dto?.Position) && string.IsNullOrWhiteSpace(dto?.Title))
        {
            return this.InvalidRequest("Position or Title is required to identify the expected book.");
        }

        try
        {
            await _seriesService.IgnoreExpectedBookAsync(seriesName, dto.Position, dto.Title, ignored);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error setting ignored={Ignored} on expected book (position {Position}, title {Title}) of series {SeriesName}",
                ignored, dto.Position, dto.Title, seriesName);
            return this.UnexpectedError();
        }
    }

    private Task RefreshProgressAction(int processed, int total, int succeeded, int failed)
    {
        _statusRegistry.SetProgress(RefreshOperationKey, processed, total);
        return _organizeHub.Clients.All.SeriesRefreshProgress(
            new SeriesRefreshProgress(processed, total, succeeded, failed));
    }

    private IActionResult StartRefresh(Func<ISeriesService, Task<(int Processed, int Succeeded, int Failed, string? StopReason)>> work)
    {
        return BackgroundOperationRunner.Start(
            _refreshLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            RefreshOperationKey,
            async sp =>
            {
                var seriesService = sp.GetRequiredService<ISeriesService>();
                var (processed, succeeded, failed, stopReason) = await work(seriesService);

                await _organizeHub.Clients.All.SeriesRefreshComplete(
                    new SeriesRefreshComplete(processed, succeeded, failed, stopReason));
            },
            () => _organizeHub.Clients.All.SeriesRefreshComplete(new SeriesRefreshComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
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
        o.IgnoredBookCount,
        o.IncludeOmnibusEditions);

    /// <summary>
    /// Shared page/pageSize validation for this controller's paged endpoints, mirroring
    /// UrlCleanupController.GetDirtyUrls (including the widened skip product so an overflowing
    /// page cannot wrap negative and silently serve the first page).
    /// </summary>
    private ObjectResult? ValidatePageSelection(int page, int pageSize, string what)
    {
        if (page < 0)
        {
            return this.InvalidRequest($"page must be zero or greater for {what}.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return this.InvalidRequest($"pageSize must be between 1 and {MaxPageSize} for {what}.");
        }

        var skip = (long)page * pageSize;
        if (skip > MaxPageOffset)
        {
            return this.InvalidRequest($"page and pageSize together may not skip more than {MaxPageOffset} {what}.");
        }

        return null;
    }

    private static SeriesExpectedBookDto ToDto(SeriesExpectedBookInfo b) => new(
        b.Id, b.Title, b.Position, b.Year, b.SourceUrl, b.IsIgnored);
}
