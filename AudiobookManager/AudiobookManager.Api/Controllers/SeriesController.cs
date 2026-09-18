using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.RateLimiting;
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
    private static readonly SemaphoreSlim _missingBookApplyLock = new(1, 1);

    public const string MatchOperationKey = "series-match";
    public const string RefreshOperationKey = "series-refresh";
    public const string MissingBookApplyOperationKey = "series-missing-book-apply";
    public const string PendingApplyOperationKey = "series-refresh-apply";

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
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] bool? matched = null)
    {
        var pagingError = ValidatePageSelection(page, pageSize, "series");
        if (pagingError != null)
        {
            return pagingError;
        }

        var overviewPage = await _seriesService.GetSeriesOverviewPageAsync(page, pageSize, search, matched);
        return Ok(new SeriesOverviewPageDto(overviewPage.Items.Select(SeriesOverviewMapper.ToDto).ToList(), overviewPage.TotalCount));
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
        [FromQuery] int ownedPageSize = PagingLimits.DefaultPageSize,
        [FromQuery] int missingPage = 0,
        [FromQuery] int missingPageSize = PagingLimits.DefaultPageSize,
        [FromQuery] int ignoredPage = 0,
        [FromQuery] int ignoredPageSize = PagingLimits.DefaultPageSize,
        [FromQuery] int partMismatchPage = 0,
        [FromQuery] int partMismatchPageSize = PagingLimits.DefaultPageSize)
    {
        foreach (var check in new[]
        {
            ValidatePageSelection(ownedPage, ownedPageSize, "owned books"),
            ValidatePageSelection(missingPage, missingPageSize, "missing books"),
            ValidatePageSelection(ignoredPage, ignoredPageSize, "ignored books"),
            ValidatePageSelection(partMismatchPage, partMismatchPageSize, "part mismatches"),
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
            ignoredSkip: (int)((long)ignoredPage * ignoredPageSize), ignoredTake: ignoredPageSize,
            partMismatchSkip: (int)((long)partMismatchPage * partMismatchPageSize), partMismatchTake: partMismatchPageSize);
        if (detail is null)
        {
            return NotFound();
        }

        return new SeriesDetailDto(
            SeriesOverviewMapper.ToDto(detail.Overview),
            new SeriesOwnedBookPageDto(detail.OwnedBooks.Select(b => new SeriesOwnedBookDto(
                b.Id, b.BookName, b.SeriesPart, b.Year, b.Authors, b.Narrators, b.DurationInSeconds, b.CoverFilePath)).ToList(), detail.OwnedBookTotal),
            new SeriesExpectedBookPageDto(detail.MissingBooks.Select(ToDto).ToList(), detail.MissingBookTotal),
            new SeriesExpectedBookPageDto(detail.IgnoredBooks.Select(ToDto).ToList(), detail.IgnoredBookTotal),
            new SeriesPartMismatchPageDto(detail.PartMismatches.Select(ToMismatchDto).ToList(), detail.PartMismatchTotal));
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
            return SeriesOverviewMapper.ToDto(overview);
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
            return SeriesOverviewMapper.ToDto(overview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting IncludeOmnibusEditions={Include} for series {SeriesName}", dto.IncludeOmnibusEditions, seriesName);
            return this.UnexpectedError();
        }
    }

    /// <summary>
    /// The regex mapping patterns owned by one series, in insertion order. A pattern has no
    /// target of its own - when the incoming metadata series value matches, it is rewritten to
    /// the owning series' name - so there is no mappedSeries field on the wire shape, and the
    /// series-scoped list is the whole of this series' pattern management surface. The list is
    /// capped at the query boundary at a curator-sized limit
    /// (<c>SeriesMappingRepository.MaxMappingsPerSeries</c>, the bounded-list invariant's
    /// explicit limit - patterns are human-maintained regex rows, so a real series stays far
    /// under it), rather than returned unbounded like the global grouped list it replaces.
    /// </summary>
    [HttpGet("mappings")]
    public async Task<ActionResult<List<SeriesMapping>>> GetSeriesMappings([FromQuery] string seriesName)
    {
        try
        {
            return await _seriesService.GetSeriesMappingsAsync(seriesName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching series mappings for {SeriesName}", seriesName);
            return this.UnexpectedError();
        }
    }

    [HttpPost("mappings")]
    public async Task<ActionResult<SeriesMapping>> CreateSeriesMapping([FromQuery] string seriesName, [FromBody] SeriesMapping? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Regex))
        {
            return this.InvalidRequest("A regex pattern is required.");
        }

        if (dto.Id is not null && dto.Id != default(long))
        {
            return this.InvalidRequest("The frontend may not specify an id for a new mapping.");
        }

        try
        {
            return await _seriesService.CreateSeriesMappingAsync(seriesName, dto);
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating a series mapping for {SeriesName}", seriesName);
            return this.UnexpectedError();
        }
    }

    [HttpPut("mappings/{mappingId}")]
    public async Task<ActionResult<SeriesMapping>> UpdateSeriesMapping(long mappingId, [FromQuery] string seriesName, [FromBody] SeriesMapping? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Regex))
        {
            return this.InvalidRequest("A regex pattern is required.");
        }

        try
        {
            var updated = await _seriesService.UpdateSeriesMappingAsync(seriesName, mappingId, dto);
            return updated is null ? NotFound() : updated;
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating series mapping {MappingId} for {SeriesName}", mappingId, seriesName);
            return this.UnexpectedError();
        }
    }

    [HttpDelete("mappings/{mappingId}")]
    public async Task<IActionResult> DeleteSeriesMapping(long mappingId, [FromQuery] string seriesName)
    {
        try
        {
            var deleted = await _seriesService.DeleteSeriesMappingAsync(seriesName, mappingId);
            return deleted ? Ok() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting series mapping {MappingId} for {SeriesName}", mappingId, seriesName);
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

    /// <summary>
    /// Refreshes one series from its matched source, synchronously. The fetch is bounded by the
    /// scraper's own HTTP timeouts, the same latency profile the single-book metadata refresh
    /// has. Refreshing always re-fetches and re-stores the roster and stamps LastRefreshedAt;
    /// the result reports whether anything changed, and a refresh that found changes also stores
    /// a pending snapshot the review dialog applies.
    ///
    /// The <see cref="_refreshLock"/> held here is the SAME gate the bulk refresh (and the
    /// pending apply, which recomputes the same pending state) uses, so a single refresh never
    /// runs concurrently with a sweep that is re-fetching the same rosters - and a busy gate
    /// returns 409 immediately, exactly like the fire-and-forget endpoints, rather than parking
    /// the request thread.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<SeriesRefreshResultDto>> RefreshSeries([FromQuery] string seriesName)
    {
        if (!_refreshLock.Wait(0))
        {
            return this.ConflictingState("A series refresh or pending apply is already in progress.", "Operation in progress");
        }

        try
        {
            var result = await _seriesService.RefreshSeriesAsync(seriesName);
            return Ok(new SeriesRefreshResultDto(result.Success, result.HasChanges, result.ChangeCount, result.SourceName));
        }
        catch (KeyNotFoundException)
        {
            // The name is the whole message - the caller supplied it and can see it.
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            // A matched source with no series-capable scraper in this build (e.g. the source was
            // removed since the series was matched). The message names the source and the remedy,
            // both safe for the caller - a 4xx, not a 500 about server state.
            return this.InvalidRequest(ex.Message);
        }
        catch (HardcoverDailyLimitExceededException ex)
        {
            // 4xx detail is relayed to the user by design; this message names the cause and the
            // remedy without leaking anything about the environment.
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing series {SeriesName}", seriesName);
            return this.UnexpectedError();
        }
        finally
        {
            _refreshLock.Release();
        }
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

    /// <summary>
    /// One page of the bulk missing-book match view: each missing roster entry of the series with
    /// its ranked candidate library audiobooks, so the client can review every missing book at
    /// once and apply all accepted assignments in a single follow-up (<c>expected-books/apply-bulk</c>).
    /// Paged like the detail sections; the per-row candidate lists are capped server-side, so the
    /// response is bounded on both axes.
    /// </summary>
    [HttpGet("expected-books/bulk-candidates")]
    public async Task<ActionResult<SeriesBulkCandidatePageDto>> GetBulkMissingBookCandidates(
        [FromQuery] string seriesName,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize)
    {
        var pagingError = ValidatePageSelection(page, pageSize, "missing books");
        if (pagingError != null)
        {
            return pagingError;
        }

        try
        {
            var result = await _seriesService.GetBulkMissingBookCandidatesAsync(
                seriesName, skip: (int)((long)page * pageSize), take: pageSize);
            return new SeriesBulkCandidatePageDto(
                result.Items.Select(i => new SeriesBulkCandidateItemDto(
                    ToDto(i.Book),
                    i.Candidates.Select(c => new SeriesBookCandidateDto(
                        c.AudiobookId, c.BookName, c.Series, c.SeriesPart, c.Year, c.Authors, c.TitleSimilarity, c.AuthorMatches)).ToList())).ToList(),
                result.TotalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching bulk missing-book candidates for series {SeriesName}", seriesName);
            return this.UnexpectedError();
        }
    }

    /// <summary>
    /// Fire-and-forget bulk application of accepted missing-book assignments. Selections are the
    /// {"Do not assign", "best candidate", "alternate candidate"} choices the review dialog made,
    /// each addressed by the roster entry's natural key plus the chosen audiobook; only the
    /// accepted rows are sent (a skipped row is simply absent). Each assignment runs through
    /// <c>ApplyMissingBookAsync</c> - the same UpdateAudiobook path the interactive apply uses -
    /// under the per-audiobook save gate, so a busy book fails just its own item and the batch
    /// carries on. The follow-up consistency recheck per book mirrors the interactive apply's tail.
    /// Progress is reported over SignalR; the operation status is recorded under
    /// <see cref="MissingBookApplyOperationKey"/> so a client can recover it after a reconnect.
    /// </summary>
    [HttpPost("expected-books/apply-bulk")]
    public async Task<IActionResult> StartBulkApplyMissingBooks([FromQuery] string seriesName, [FromBody] ApplyMissingBookBulkRequestDto? dto)
    {
        var selections = dto?.Selections;
        if (selections is null || selections.Count == 0)
        {
            return this.InvalidRequest("At least one book assignment is required.");
        }

        foreach (var selection in selections)
        {
            if (selection.AudiobookId <= 0)
            {
                return this.InvalidRequest("A valid AudiobookId is required for every assignment.");
            }

            if (string.IsNullOrWhiteSpace(selection.Position) && string.IsNullOrWhiteSpace(selection.Title))
            {
                return this.InvalidRequest("Position or Title is required for every assignment.");
            }
        }

        // One library book can only be assigned to one missing slot: a duplicate would apply the
        // series and part to the same audiobook twice (two writes to one book, two moves), with
        // the second clobbering the first and both counting as "succeeded". Reject the batch up
        // front rather than discovering the collision mid-run. AudiobookId is validated > 0 above,
        // so a zero FirstOrDefault is a reliable "no duplicate" sentinel.
        var duplicateAudiobookId = selections
            .GroupBy(s => s.AudiobookId)
            .FirstOrDefault(g => g.Count() > 1)
            ?.Key ?? 0;
        if (duplicateAudiobookId != 0)
        {
            return this.InvalidRequest(
                $"A library book can only be assigned to one missing book: audiobook {duplicateAudiobookId} appears more than once in the request.");
        }

        // Symmetric guard on the other side of that assignment: two selections may not target the
        // same roster entry. The natural keys are RESOLVED rather than string-compared, because
        // the strict matching rule ApplyMissingBookAsync uses - both parts when both are supplied,
        // position OR title alone otherwise, trimmed and case-insensitive - means "2" on its own
        // and "2" + "The Well of Ascension" can name the same row, which a raw key-pair equality
        // check would miss. The resolution happens here, before the background operation starts,
        // so a duplicate target fails this request instead of a mid-batch double-apply.
        var resolvedTargets = new HashSet<long>();
        foreach (var selection in selections)
        {
            var expected = await _seriesService.ResolveExpectedBookAsync(
                seriesName, selection.Position, selection.Title);
            if (expected is null)
            {
                continue;
            }

            if (!resolvedTargets.Add(expected.Id))
            {
                return this.InvalidRequest(
                    $"A missing book can only be assigned once: roster entry {DescribeExpected(expected)} is targeted by more than one assignment.");
            }
        }

        return BackgroundOperationRunner.Start(
            _missingBookApplyLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            MissingBookApplyOperationKey,
            async sp =>
            {
                // Resolved from the background scope, never from the request's own instances:
                // both services are scoped, and the background task outlives the request.
                var seriesService = sp.GetRequiredService<ISeriesService>();
                var libraryConsistencyService = sp.GetRequiredService<ILibraryConsistencyService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(MissingBookApplyOperationKey, processed, total);
                    return _organizeHub.Clients.All.SeriesMissingBookApplyProgress(
                        new SeriesMissingBookApplyProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed) = await ApplyMissingBookSelectionsCoreAsync(
                    seriesService, libraryConsistencyService, seriesName, selections, ProgressAction);

                await _organizeHub.Clients.All.SeriesMissingBookApplyComplete(
                    new SeriesMissingBookApplyComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.SeriesMissingBookApplyComplete(new SeriesMissingBookApplyComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    /// <summary>
    /// The bulk apply loop. The save gate, the apply, and the follow-up recheck are exactly the
    /// interactive <c>ApplyExpectedBook</c> action's body, run once per accepted selection with
    /// the shared bulk contract: one try/catch per item so a single failure never aborts the
    /// batch, and a (processed, total, succeeded, failed) progress report after every item. A
    /// recheck failure is non-fatal, mirroring the interactive apply.
    /// </summary>
    private async Task<(int Processed, int Succeeded, int Failed)> ApplyMissingBookSelectionsCoreAsync(
        ISeriesService seriesService,
        ILibraryConsistencyService libraryConsistencyService,
        string seriesName,
        IReadOnlyList<ApplyMissingBookSelectionDto> selections,
        Func<int, int, int, int, Task> progressAction)
    {
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = selections.Count;

        foreach (var selection in selections)
        {
            processed++;
            try
            {
                // Same per-audiobook gate the interactive apply (and the save PUT) holds: two
                // writers must never rewrite the same book's tags concurrently. A book someone
                // is saving right now fails just its own item and the batch carries on.
                using var lease = _saveGate.Acquire(selection.AudiobookId);

                await seriesService.ApplyMissingBookAsync(seriesName, selection.Position, selection.Title, selection.AudiobookId);

                // Same tail as the interactive apply: the assignment rewrote tags and possibly
                // moved the file, so stored issues for this book are stale until rechecked.
                try
                {
                    await libraryConsistencyService.RecheckAudiobookAsync(selection.AudiobookId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after a bulk series assignment", selection.AudiobookId);
                }

                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Bulk series assignment failed for series {SeriesName}, expected book (position {Position}, title {Title}), audiobook {AudiobookId}",
                    seriesName, selection.Position, selection.Title, selection.AudiobookId);
                failed++;
            }

            await progressAction(processed, total, succeeded, failed);
        }

        return (processed, succeeded, failed);
    }

    /// <summary>
    /// One page of the pending series-refresh list - the series whose last refresh found
    /// explicit changes, newest fetch first. Rows exist only for refresh runs that produced
    /// changes (a no-change bulk item never appears here), so this page IS the "something to
    /// review" surface, not a log of every refresh.
    /// </summary>
    [HttpGet("pending")]
    public async Task<ActionResult<SeriesRefreshPendingPageDto>> GetPendingPage(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize)
    {
        var pagingError = ValidatePageSelection(page, pageSize, "pending series refreshes");
        if (pagingError != null)
        {
            return pagingError;
        }

        try
        {
            var (items, total) = await _seriesService.GetPendingSeriesRefreshPageAsync(page, pageSize);
            return Ok(new SeriesRefreshPendingPageDto(
                items.Select(ToListItemDto).ToList(),
                total));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching the pending series-refresh page");
            return this.UnexpectedError();
        }
    }

    /// <summary>The number of series with a pending snapshot, for the list header badge.</summary>
    [HttpGet("pending/count")]
    public async Task<ActionResult<int>> GetPendingCount()
    {
        try
        {
            return Ok(await _seriesService.CountPendingSeriesRefreshesAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting pending series refreshes");
            return this.UnexpectedError();
        }
    }

    /// <summary>The stored pending snapshot for one series, for the review dialog.</summary>
    [HttpGet("pending/detail")]
    public async Task<ActionResult<SeriesRefreshPendingDto>> GetPendingDetail([FromQuery] string seriesName)
    {
        try
        {
            var pending = await _seriesService.GetPendingSeriesRefreshAsync(seriesName);
            if (pending is null)
            {
                return NotFound();
            }

            return Ok(new SeriesRefreshPendingDto(
                pending.SeriesName,
                pending.SourceName,
                pending.SourceUrl,
                pending.SourceSeriesName,
                pending.FetchedAt,
                pending.Changes.Select(ToChangeDto).ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching the pending series refresh detail for {SeriesName}", seriesName);
            return this.UnexpectedError();
        }
    }

    /// <summary>
    /// Deletes the pending snapshot for a series - after the user applied (or decided to
    /// discard) the changes. Dismissing an already-dismissed snapshot is a no-op success, not a
    /// failure: the series is already in the state the caller asked for.
    ///
    /// Takes the same <see cref="_refreshLock"/> the refresh and the pending apply hold, for the
    /// same reason they hold it against each other: all three read and then replace this row. A
    /// dismiss landing between an apply's recompute and its upsert deleted a row the apply then
    /// put straight back, so the snapshot the user dismissed reappeared.
    /// </summary>
    [HttpPost("pending/dismiss")]
    public async Task<IActionResult> DismissPending([FromQuery] string seriesName)
    {
        if (!_refreshLock.Wait(0))
        {
            return this.ConflictingState("A series refresh or pending apply is already in progress.", "Operation in progress");
        }

        try
        {
            await _seriesService.DismissPendingSeriesRefreshAsync(seriesName);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dismissing the pending series refresh for {SeriesName}", seriesName);
            return this.UnexpectedError();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Fire-and-forget application of the accepted pending changes for one series. The selections
    /// are the {"PartUpdate", "MissingBook", "PartRemoval"} rows the review dialog accepted; part
    /// updates and removals are addressed by the target audiobook id, missing books by the roster
    /// natural key (position/title) plus the chosen library audiobook. Each change runs through
    /// the same UpdateAudiobook pipeline the interactive edit uses, under the per-audiobook save
    /// gate, so a busy book fails just its own item and the batch carries on. The request's
    /// optional <c>AdoptSourceSeriesName</c> renames every member book to the source's own series
    /// name through the same pipeline. Progress is reported over SignalR; the operation status is
    /// recorded under <see cref="PendingApplyOperationKey"/> so a client can recover it after a
    /// reconnect.
    /// </summary>
    [HttpPost("pending/apply")]
    public IActionResult StartPendingApply([FromQuery] string seriesName, [FromBody] ApplySeriesRefreshRequestDto? dto)
    {
        if (dto is null || (dto.Selections.Count == 0 && !dto.AdoptSourceSeriesName))
        {
            return this.InvalidRequest("At least one accepted change, or the source-series-name adoption, is required.");
        }

        foreach (var selection in dto.Selections)
        {
            if (SeriesRefreshChangeTypeDto.FromDto(selection.ChangeType) is null)
            {
                return this.InvalidRequest($"Unknown ChangeType '{selection.ChangeType}'.");
            }

            if (selection.AudiobookId is not > 0)
            {
                return this.InvalidRequest("A valid AudiobookId is required for every accepted change.");
            }
        }

        // One library book is written once per apply: two selections touching the same book
        // would both count as succeeded while the second clobbers the first, exactly the
        // duplicate-selection collision the bulk missing-book apply rejects up front.
        var duplicateAudiobookId = dto.Selections
            .Where(s => s.AudiobookId is > 0)
            .GroupBy(s => s.AudiobookId)
            .FirstOrDefault(g => g.Count() > 1)
            ?.Key ?? 0;
        if (duplicateAudiobookId != 0)
        {
            return this.InvalidRequest(
                $"A library book can only be changed once: audiobook {duplicateAudiobookId} appears more than once in the request.");
        }

        // Symmetric guard on the other side: two MissingBook selections may not target the same
        // roster entry. The natural keys are compared the way the apply resolves them - trimmed,
        // case-insensitive - so "4" + "Book B" and "4" + " book b " name the same row even though
        // the raw strings differ. A duplicate would assign two different library books to one
        // missing slot.
        var missingBookTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in dto.Selections)
        {
            if (SeriesRefreshChangeTypeDto.FromDto(selection.ChangeType) != SeriesRefreshChangeType.MissingBook)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(selection.Position) && string.IsNullOrWhiteSpace(selection.Title))
            {
                return this.InvalidRequest("Every MissingBook change needs a Position or Title to identify the roster entry.");
            }

            var key = $"{selection.Position?.Trim() ?? ""}\u0001{selection.Title?.Trim() ?? ""}";
            if (!missingBookTargets.Add(key))
            {
                return this.InvalidRequest("A missing book can only be applied once: more than one selection targets the same roster entry.");
            }
        }

        var request = new SeriesRefreshApplyRequest(
            dto.AdoptSourceSeriesName,
            dto.Selections
                .Select(s => new SeriesRefreshApplyChange(
                    SeriesRefreshChangeTypeDto.FromDto(s.ChangeType)!.Value,
                    s.AudiobookId,
                    s.Position,
                    s.Title))
                .ToList());

        // The apply shares the refresh gate on purpose: both operations read and then replace the
        // same pending snapshot, so they must be mutually exclusive or a refresh running mid-apply
        // could wipe the row the apply is about to recompute (and vice versa).
        return BackgroundOperationRunner.Start(
            _refreshLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            PendingApplyOperationKey,
            async sp =>
            {
                var seriesService = sp.GetRequiredService<ISeriesService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(PendingApplyOperationKey, processed, total);
                    return _organizeHub.Clients.All.SeriesRefreshApplyProgress(
                        new SeriesRefreshApplyProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed) = await seriesService.ApplyPendingSeriesRefreshAsync(
                    seriesName, request, ProgressAction);

                await _organizeHub.Clients.All.SeriesRefreshApplyComplete(
                    new SeriesRefreshApplyComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.SeriesRefreshApplyComplete(new SeriesRefreshApplyComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
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

        if (pageSize < 1 || pageSize > PagingLimits.MaxPageSize)
        {
            return this.InvalidRequest($"pageSize must be between 1 and {PagingLimits.MaxPageSize} for {what}.");
        }

        var skip = (long)page * pageSize;
        if (skip > PagingLimits.MaxPageOffset)
        {
            return this.InvalidRequest($"page and pageSize together may not skip more than {PagingLimits.MaxPageOffset} {what}.");
        }

        return null;
    }

    private static SeriesExpectedBookDto ToDto(SeriesExpectedBookInfo b) => new(
        b.Id, b.Title, b.Position, b.Year, b.SourceUrl, b.IsIgnored);

    private static SeriesPartMismatchDto ToMismatchDto(SeriesPartMismatch m) => new(
        m.AudiobookId, m.BookName, m.StoredPart, m.ExpectedPart, m.RosterTitle);

    private static SeriesRefreshPendingListItemDto ToListItemDto(PendingSeriesRefreshListItem item) => new(
        item.SeriesName,
        item.SourceName,
        item.SourceSeriesName,
        item.FetchedAt,
        item.ChangeCount);

    private static SeriesRefreshChangeDto ToChangeDto(SeriesRefreshChange change) => new(
        SeriesRefreshChangeTypeDto.ToDto(change.Type),
        change.AudiobookId,
        change.BookName,
        change.StoredPart,
        change.NewPart,
        change.RosterTitle,
        change.Position,
        change.Title,
        change.Year);

    /// <summary>
    /// Human-readable label of a resolved roster entry for an error the caller sees. Uses the
    /// canonical values from the stored row (not the key the client sent), so the label agrees
    /// with what the series detail's missing section shows even when the two selections that
    /// collided named the entry differently.
    /// </summary>
    private static string DescribeExpected(SeriesExpectedBookInfo book)
    {
        var position = book.Position;
        return string.IsNullOrWhiteSpace(position)
            ? $"'{book.Title}'"
            : $"'{book.Title}' (position '{position}')";
    }
}
