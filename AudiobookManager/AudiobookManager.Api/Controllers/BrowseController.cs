using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BrowseController : ControllerBase
{
    // The process-wide expected-book write gate shared with SeriesController: the author refresh
    // and the series refresh write the SAME unified expected-books rows (one row serves an
    // author's bibliography and its series' roster), so a per-controller semaphore could not stop
    // an author refresh from racing a series refresh into a read-then-upsert-then-prune on the
    // same rows. All three endpoints reach
    // IUpcomingReleaseService.RefreshAuthorRosterAsync/RefreshAllAuthorRostersAsync (and the
    // match endpoint runs that refresh as well) under this one gate, so two concurrent roster
    // mutations - of either scope - are impossible; a busy gate returns 409 immediately, exactly
    // like the fire-and-forget endpoints, rather than parking the request thread.
    private readonly IExpectedBookWriteGate _expectedBookWriteGate;

    public const string RefreshAllOperationKey = "author-roster-refresh-all";

    private readonly IAudiobookRepository _audiobookRepo;
    private readonly IPersonRepository _personRepo;
    private readonly IGenreRepository _genreRepo;
    private readonly ISeriesService _seriesService;
    private readonly IUpcomingReleaseService _upcomingReleaseService;
    private readonly IAuthorReconciliationProvider _authorReconciliation;
    private readonly IEnumerable<IScraper> _scrapers;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<BrowseController> _logger;

    public BrowseController(
        IAudiobookRepository audiobookRepo,
        IPersonRepository personRepo,
        IGenreRepository genreRepo,
        ISeriesService seriesService,
        IUpcomingReleaseService upcomingReleaseService,
        IAuthorReconciliationProvider authorReconciliation,
        IExpectedBookWriteGate expectedBookWriteGate,
        IEnumerable<IScraper> scrapers,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IHostApplicationLifetime appLifetime,
        ILogger<BrowseController> logger)
    {
        _audiobookRepo = audiobookRepo;
        _personRepo = personRepo;
        _genreRepo = genreRepo;
        _seriesService = seriesService;
        _upcomingReleaseService = upcomingReleaseService;
        _authorReconciliation = authorReconciliation;
        _expectedBookWriteGate = expectedBookWriteGate;
        _scrapers = scrapers;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    /// <summary>
    /// The metadata source names available to filter by (whichever scrapers are actually
    /// registered - see AudiobookManager.Scraping.DependencyInjection - not a hardcoded list), so
    /// the book/author/series source filter dropdowns never offer a source this deployment cannot
    /// produce, plus the synthetic "Unsupported" bucket every one of those filters also offers.
    /// </summary>
    [HttpGet("filter-options")]
    public async Task<BrowseFilterOptionsDto> GetFilterOptions()
    {
        var sources = _scrapers
            .Select(s => s.SourceName)
            .Distinct()
            .OrderBy(s => s, StringComparer.InvariantCulture)
            .Append(BookSummaryFilter.UnsupportedSource)
            .ToList();

        var genres = await _genreRepo.GetAllGenreNamesAsync();
        var languages = await _audiobookRepo.GetAllLanguagesAsync();

        return new BrowseFilterOptionsDto(sources, genres, languages);
    }

    [HttpGet("audiobooks")]
    public async Task<PaginatedResult<AudiobookSummaryDto>> GetAudiobooks(
        int limit = 20, int offset = 0,
        [FromQuery] List<string>? sources = null,
        [FromQuery] List<string>? genres = null,
        [FromQuery] List<string>? languages = null,
        [FromQuery] int? minDurationInSeconds = null,
        [FromQuery] int? maxDurationInSeconds = null)
    {
        var filter = new BookSummaryFilter(sources, genres, languages, minDurationInSeconds, maxDurationInSeconds);
        var (items, total) = await _audiobookRepo.GetAllAsync(limit, offset, filter.IsEmpty ? null : filter);
        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PaginatedResult<AudiobookSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("library-search")]
    public async Task<LibrarySearchResultDto> SearchLibrary([FromQuery] string q, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return new LibrarySearchResultDto([], [], []);
        }

        var (books, _) = await _audiobookRepo.SearchAsync(
            q, limit, 0, includeTotal: false, includeNarratorsAndGenres: false);
        var (authors, _) = await _personRepo.SearchAuthorSummariesAsync(q, limit, 0);
        var (series, _) = await _audiobookRepo.SearchSeriesAsync(q, limit, 0);

        // No client-side re-ranking: the repositories now rank prefix matches in SQL, before
        // their LIMIT, so the rows that arrive are already the best ones in the right order. The
        // old RankByRelevance pass also compared with a plain OrdinalIgnoreCase StartsWith,
        // which is not accent-insensitive - it demoted a "René" row that had correctly
        // prefix-matched a "Rene" query.
        var bookHits = books
            .Select(a => new LibraryBookHitDto(
                a.Id,
                a.BookName,
                a.Subtitle,
                a.Authors.Select(p => p.Name).ToList(),
                a.Series,
                a.Year,
                a.CoverFilePath))
            .ToList();

        var authorHits = authors
            .Select(p => new LibraryAuthorHitDto(p.Id, p.Name, p.BookCount))
            .ToList();

        var seriesHits = series
            .Select(s => new LibrarySeriesHitDto(s.Series, s.BookCount))
            .ToList();

        return new LibrarySearchResultDto(bookHits, authorHits, seriesHits);
    }

    [HttpGet("audiobooks/search")]
    public async Task<PaginatedResult<AudiobookSummaryDto>> SearchAudiobooks(
        [FromQuery] string q, int limit = 20, int offset = 0,
        [FromQuery] List<string>? sources = null,
        [FromQuery] List<string>? genres = null,
        [FromQuery] List<string>? languages = null,
        [FromQuery] int? minDurationInSeconds = null,
        [FromQuery] int? maxDurationInSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return await GetAudiobooks(limit, offset, sources, genres, languages, minDurationInSeconds, maxDurationInSeconds);
        }

        var filter = new BookSummaryFilter(sources, genres, languages, minDurationInSeconds, maxDurationInSeconds);
        var (items, total) = await _audiobookRepo.SearchAsync(q, limit, offset, filter: filter.IsEmpty ? null : filter);
        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PaginatedResult<AudiobookSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("authors/search")]
    public async Task<ActionResult<PaginatedResult<AuthorSummaryDto>>> SearchAuthors(
        [FromQuery] string q, int limit = 20, int offset = 0)
    {
        var clampError = ValidateSearchPaging(limit, offset);
        if (clampError != null)
        {
            return clampError;
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return new PaginatedResult<AuthorSummaryDto>(0, 0, []);
        }

        var (items, total) = await _personRepo.SearchAuthorSummariesAsync(q, limit, offset);
        var dtos = items.Select(p => new AuthorSummaryDto(p.Id, p.Name, p.BookCount)).ToList();
        return new PaginatedResult<AuthorSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("series/search")]
    public async Task<ActionResult<PaginatedResult<LibrarySeriesHitDto>>> SearchSeries(
        [FromQuery] string q, int limit = 20, int offset = 0)
    {
        var clampError = ValidateSearchPaging(limit, offset);
        if (clampError != null)
        {
            return clampError;
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return new PaginatedResult<LibrarySeriesHitDto>(0, 0, []);
        }

        var (items, total) = await _audiobookRepo.SearchSeriesAsync(q, limit, offset);
        var dtos = items.Select(s => new LibrarySeriesHitDto(s.Series, s.BookCount)).ToList();
        return new PaginatedResult<LibrarySeriesHitDto>(dtos.Count, total, dtos);
    }

    /// <summary>
    /// Shared limit/offset validation for the paged author/series search endpoints. Mirrors the
    /// clamping shape in UrlCleanupController.GetDirtyUrls.
    /// </summary>
    private ObjectResult? ValidateSearchPaging(int limit, int offset)
    {
        if (limit < 1 || limit > PagingLimits.MaxSearchPageSize)
        {
            return Problem(
                detail: $"limit must be between 1 and {PagingLimits.MaxSearchPageSize}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        if (offset < 0 || offset > PagingLimits.MaxSearchOffset)
        {
            return Problem(
                detail: $"offset must be between 0 and {PagingLimits.MaxSearchOffset}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        return null;
    }

    [HttpGet("authors")]
    public async Task<ActionResult<PaginatedResult<AuthorSummaryDto>>> GetAuthors(
        [FromQuery] string? q = null,
        int limit = PagingLimits.DefaultPageSize,
        int offset = 0,
        [FromQuery] bool? followed = null,
        [FromQuery] int? minBookCount = null,
        [FromQuery] int? maxBookCount = null,
        [FromQuery] bool? hasMissingBooks = null,
        [FromQuery] bool? hasUpcomingBooks = null,
        [FromQuery] bool? matched = null,
        [FromQuery] DateTime? refreshedAfter = null,
        [FromQuery] DateTime? refreshedBefore = null,
        [FromQuery] bool? neverRefreshed = null,
        [FromQuery] List<string>? sources = null)
    {
        var clampError = ValidateSearchPaging(limit, offset);
        if (clampError != null)
        {
            return clampError;
        }

        if (minBookCount is < 0 || maxBookCount is < 0)
        {
            return this.InvalidRequest("minBookCount and maxBookCount must be zero or greater.");
        }

        if (minBookCount is not null && maxBookCount is not null && minBookCount > maxBookCount)
        {
            return this.InvalidRequest("minBookCount must not be greater than maxBookCount.");
        }

        if (refreshedAfter is not null && refreshedBefore is not null && refreshedAfter > refreshedBefore)
        {
            return this.InvalidRequest("refreshedAfter must not be after refreshedBefore.");
        }

        var filter = new AuthorSummaryFilter(
            followed, minBookCount, maxBookCount, hasMissingBooks, hasUpcomingBooks, matched,
            refreshedAfter, refreshedBefore, neverRefreshed, sources);

        // HasMissingBooks/HasUpcomingBooks depend on the fuzzy roster reconciliation, which this
        // controller already holds a provider for (the author detail page's missing-books
        // section uses the same one) - resolved into a restricting id set before the paged SQL
        // query runs, exactly like SeriesService does for the series list. Only computed when the
        // caller actually asks for one of these two filters. A librarian past the bulk
        // classification's bounded read cannot be classified safely - the provider reports the
        // refusal and the missing/upcoming filter is skipped (the rest of the filters still
        // apply) rather than applied to a truncated set.
        IReadOnlyCollection<long>? restrictToIds = null;
        IReadOnlyCollection<long>? excludeIds = null;
        if (filter.NeedsReconciliation)
        {
            var bulk = await _authorReconciliation.GetBulkMissingOrUpcomingAuthorIdsAsync();
            if (bulk.Refused)
            {
                _logger.LogWarning(
                    "Skipping the missing/upcoming-book author filter: the unified expected-book roster exceeds the bounded bulk classification's reference cap.");
            }
            else
            {
                HashSet<long>? include = null;
                var exclude = new HashSet<long>();

                if (hasMissingBooks == true)
                {
                    include = bulk.HasMissingBooks;
                }
                else if (hasMissingBooks == false)
                {
                    exclude.UnionWith(bulk.HasMissingBooks);
                }

                if (hasUpcomingBooks == true)
                {
                    include = include is null ? bulk.HasUpcomingBooks : include.Intersect(bulk.HasUpcomingBooks).ToHashSet();
                }
                else if (hasUpcomingBooks == false)
                {
                    exclude.UnionWith(bulk.HasUpcomingBooks);
                }

                restrictToIds = include;
                excludeIds = exclude.Count > 0 ? exclude : null;
            }
        }

        var search = string.IsNullOrWhiteSpace(q) ? null : q!.Trim();
        var (items, total) = await _personRepo.GetAuthorSummariesPagedAsync(
            search, limit, offset, filter.IsEmpty ? null : filter, restrictToIds, excludeIds);
        var dtos = items.Select(p => new AuthorSummaryDto(p.Id, p.Name, p.BookCount)).ToList();
        return new PaginatedResult<AuthorSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("authors/{authorId}")]
    public async Task<ActionResult<AuthorDetailDto>> GetAuthorDetail(
        long authorId,
        int seriesLimit = PagingLimits.DefaultPageSize,
        int seriesOffset = 0,
        int standaloneLimit = PagingLimits.DefaultPageSize,
        int standaloneOffset = 0,
        [FromQuery] bool includeMissingSeries = false,
        [FromQuery] int missingSeriesLimit = PagingLimits.DefaultPageSize,
        [FromQuery] int missingSeriesOffset = 0)
    {
        var clampError = ValidateSearchPaging(seriesLimit, seriesOffset)
            ?? ValidateSearchPaging(standaloneLimit, standaloneOffset)
            ?? ValidateSearchPaging(missingSeriesLimit, missingSeriesOffset);
        if (clampError != null)
        {
            return clampError;
        }

        // The series section pages by page number, computed from the offset; a non-multiple
        // offset would silently truncate. seriesLimit is already validated to be >= 1 above.
        if (seriesOffset % seriesLimit != 0)
        {
            return this.InvalidRequest("seriesOffset must be a multiple of seriesLimit.");
        }

        // Three narrow queries rather than one that materializes the author's entire catalogue:
        // the series section runs through the same overview pipeline as /library/series (so its
        // entries carry match state, authors and owned/missing counts), scoped to this author's
        // series values; the standalone books are paged straight from SQL.
        var author = await _personRepo.GetAuthorSummaryAsync(authorId);
        if (author == null)
        {
            return NotFound();
        }

        var seriesPage = await _seriesService.GetSeriesOverviewPageAsync(
            page: seriesOffset / seriesLimit,
            pageSize: seriesLimit,
            search: null,
            matched: null,
            authorId: authorId);
        var (standalone, standaloneTotal) = await _audiobookRepo.GetStandaloneBooksByAuthorAsync(
            authorId, standaloneLimit, standaloneOffset);

        var summary = new AuthorSummaryDto(author.Id, author.Name, author.BookCount);
        var seriesDtos = seriesPage.Items.Select(SeriesOverviewMapper.ToDto).ToList();
        var standaloneDtos = standalone.Select(MapToSummaryDto).ToList();

        // The Hardcover-match/LastRefreshedAt fields live on the tracked Person row, like
        // GetAuthorMatch above - the cheap AuthorSummaryRow projection this endpoint otherwise
        // reads from doesn't carry them.
        var person = await _personRepo.GetByIdAsync(authorId);
        var reconciliation = await _authorReconciliation.GetReconciliationAsync(
            authorId, includeMissingSeries);

        // The missing-series groups are computed (capped per author by the reconciliation
        // provider) only when the caller asks for them and then sliced into one page here, so a
        // call that does not want the section never pays for the reconciliation pass that feeds
        // it, and a call that does want it never receives the whole capped list.
        PaginatedResult<AuthorMissingSeriesDto>? missingSeries = null;
        if (includeMissingSeries)
        {
            var missingSeriesItems = reconciliation.MissingSeries
                .Skip(missingSeriesOffset)
                .Take(missingSeriesLimit)
                .Select(ToMissingSeriesDto)
                .ToList();
            missingSeries = new PaginatedResult<AuthorMissingSeriesDto>(
                missingSeriesItems.Count, reconciliation.MissingSeries.Count, missingSeriesItems);
        }

        return new AuthorDetailDto(
            summary,
            new PaginatedResult<SeriesOverviewDto>(seriesDtos.Count, seriesPage.TotalCount, seriesDtos),
            new PaginatedResult<AudiobookSummaryDto>(standaloneDtos.Count, standaloneTotal, standaloneDtos),
            person?.LastRefreshedAt,
            reconciliation.Missing.Select(ToAuthorExpectedBookDto).ToList(),
            reconciliation.Upcoming.Select(ToAuthorExpectedBookDto).ToList(),
            reconciliation.Ignored.Select(ToAuthorExpectedBookDto).ToList(),
            missingSeries);
    }

    private static AuthorExpectedBookDto ToAuthorExpectedBookDto(AuthorExpectedBookInfo b) =>
        new(b.Id, b.Title, b.Year, b.SourceUrl, b.IsIgnored, b.ReleaseDate,
            b.Position, b.SeriesId, b.SeriesName, b.SourceSeriesName,
            b.SourceName, b.SourceBookId, b.ImageUrl);

    private static AuthorMissingSeriesDto ToMissingSeriesDto(AuthorMissingSeriesInfo m) => new(
        m.SourceName, m.SourceSeriesId, m.SourceSeriesName,
        m.ExpectedCount, m.MissingCount, m.UpcomingCount, m.OwnedCount,
        m.SeriesId, m.SeriesName);

    /// <summary>
    /// Refreshes one author's roster (unified expected books) from their matched source. Mirrors
    /// SeriesController.RefreshSeries, but without the pending-changes review step - an author
    /// refresh writes the unified roster directly (upsert + prune, see
    /// IUpcomingReleaseService.RefreshAuthorRosterAsync).
    ///
    /// Takes the SAME shared <see cref="IExpectedBookWriteGate"/> the bulk sweep below (and every
    /// series-side roster mutation) holds, so a single-author refresh can never run concurrently
    /// with the bulk sweep re-fetching the same author's roster, nor with a series refresh
    /// rewriting the same unified rows - a busy gate returns 409 immediately, exactly
    /// like the fire-and-forget endpoints, rather than parking the request thread.
    /// </summary>
    [HttpPost("authors/{authorId}/refresh")]
    public async Task<ActionResult<AuthorRefreshResultDto>> RefreshAuthor(long authorId)
    {
        if (!_expectedBookWriteGate.TryAcquire())
        {
            return this.ConflictingState("An author-roster refresh is already in progress.", "Operation in progress");
        }

        try
        {
            await _upcomingReleaseService.RefreshAuthorRosterAsync(authorId);
            var person = await _personRepo.GetByIdAsync(authorId);
            return new AuthorRefreshResultDto(true, person?.LastRefreshedAt);
        }
        catch (KeyNotFoundException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (HardcoverDailyLimitExceededException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing roster for author {AuthorId}", authorId);
            return this.UnexpectedError();
        }
        finally
        {
            _expectedBookWriteGate.Release();
        }
    }

    /// <summary>
    /// Refreshes the roster of every matched author, fire-and-forget - mirroring
    /// SeriesController's refresh-all rather than the synchronous single-author refresh above.
    /// This issues one rate-limited request per matched author (burst 5, <=55/min), so a library
    /// with a meaningful number of matched authors can run for minutes; awaiting that on the
    /// request thread would commonly hit a reverse proxy's or browser's timeout while the sweep
    /// kept running server-side. No SignalR progress stream - like
    /// MissingTagsController.StartLanguageBackfill, the client follows it by polling
    /// GET api/operations/author-roster-refresh-all/status. Holds the SAME shared
    /// <see cref="IExpectedBookWriteGate"/> the single-author refresh (and the series roster
    /// mutations) take, so the two can never run concurrently against the same rosters.
    /// </summary>
    [HttpPost("authors/refresh-all")]
    public IActionResult RefreshAllAuthors()
    {
        return BackgroundOperationRunner.Start(
            _expectedBookWriteGate,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            RefreshAllOperationKey,
            async sp =>
            {
                var upcomingReleaseService = sp.GetRequiredService<IUpcomingReleaseService>();
                var (processed, succeeded, failed, stopReason) =
                    await upcomingReleaseService.RefreshAllAuthorRostersAsync();

                _logger.LogInformation(
                    "Author-roster refresh-all finished. Processed: {Processed}, Succeeded: {Succeeded}, Failed: {Failed}, StopReason: {StopReason}",
                    processed, succeeded, failed, stopReason ?? "(none)");
            },
            () => Task.CompletedTask,
            _appLifetime.ApplicationStopping);
    }

    // Roster entries are addressed by the stable expected-book row id (preferred - the id the
    // unified row keeps across refreshes, so a person with two same-titled entries can ignore the
    // exact row) with the title route kept as the compatibility fallback. Internally the service
    // has id-addressing paths (UpcomingReleaseService.DismissAuthorRosterUpcomingByIdAsync) that
    // avoid the title ambiguity entirely; the title-addressed routes here remain as the
    // compatibility surface, mirroring SeriesController's IgnoreExpectedBook/UnignoreExpectedBook
    // pair on the SAME shared expected-book row the author and series scopes read from, so
    // dismissing here hides the book everywhere.
    [HttpPost("authors/{authorId}/expected-books/ignore")]
    public Task<IActionResult> IgnoreExpectedBook(long authorId, [FromBody] AuthorExpectedBookRefDto dto) =>
        SetExpectedBookIgnored(authorId, dto, true);

    [HttpPost("authors/{authorId}/expected-books/unignore")]
    public Task<IActionResult> UnignoreExpectedBook(long authorId, [FromBody] AuthorExpectedBookRefDto dto) =>
        SetExpectedBookIgnored(authorId, dto, false);

    private async Task<IActionResult> SetExpectedBookIgnored(long authorId, AuthorExpectedBookRefDto? dto, bool ignored)
    {
        // The stable expected-book row id is the preferred addressing; the title route is the
        // compatibility fallback for callers that only carry the natural key.
        if (dto?.Id is not long id)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
            {
                return this.InvalidRequest("Expected book Id or Title is required to identify the expected book.");
            }

            try
            {
                if (ignored)
                {
                    await _upcomingReleaseService.DismissAuthorRosterUpcomingAsync(authorId, dto.Title);
                }
                else
                {
                    await _upcomingReleaseService.RestoreAuthorRosterUpcomingAsync(authorId, dto.Title);
                }

                return Ok();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error setting ignored={Ignored} on expected book (title {Title}) of author {AuthorId}",
                    ignored, dto.Title, authorId);
                return this.UnexpectedError();
            }
        }

        try
        {
            if (ignored)
            {
                await _upcomingReleaseService.DismissAuthorRosterUpcomingByIdAsync(id);
            }
            else
            {
                await _upcomingReleaseService.RestoreAuthorRosterUpcomingByIdAsync(id);
            }

            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error setting ignored={Ignored} on expected book (id {ExpectedBookId}) of author {AuthorId}",
                ignored, id, authorId);
            return this.UnexpectedError();
        }
    }

    [HttpGet("authors/{authorId}/follow")]
    public async Task<ActionResult<AuthorFollowStatusDto>> GetAuthorFollowStatus(long authorId)
    {
        return new AuthorFollowStatusDto(await _upcomingReleaseService.IsAuthorFollowedAsync(authorId));
    }

    [HttpPost("authors/{authorId}/follow")]
    public async Task<IActionResult> FollowAuthor(long authorId)
    {
        try
        {
            await _upcomingReleaseService.FollowAuthorAsync(authorId);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error following author {AuthorId}", authorId);
            return this.UnexpectedError();
        }
    }

    [HttpDelete("authors/{authorId}/follow")]
    public async Task<IActionResult> UnfollowAuthor(long authorId)
    {
        try
        {
            await _upcomingReleaseService.UnfollowAuthorAsync(authorId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unfollowing author {AuthorId}", authorId);
            return this.UnexpectedError();
        }
    }

    [HttpGet("authors/{authorId}/hardcover-match")]
    public async Task<ActionResult<AuthorMatchStatusDto>> GetAuthorMatch(long authorId)
    {
        var author = await _personRepo.GetAuthorSummaryAsync(authorId);
        if (author is null)
        {
            return NotFound();
        }

        // The author's Hardcover match lives on the tracked Person row, not the read-only
        // AuthorSummaryRow projection - fetched separately since every other author endpoint on
        // this controller intentionally stays on the cheap summary projection.
        var person = await _personRepo.GetByIdAsync(authorId);
        return new AuthorMatchStatusDto(person?.MatchedSourceId, person?.MatchedSourceName, person?.MatchedSourceUrl);
    }

    [HttpGet("authors/{authorId}/hardcover-match-candidates")]
    public async Task<ActionResult<List<AuthorMatchCandidateDto>>> GetAuthorMatchCandidates(long authorId, [FromQuery] string? query = null)
    {
        var author = await _personRepo.GetAuthorSummaryAsync(authorId);
        if (author is null)
        {
            return NotFound();
        }

        try
        {
            var searchTerm = string.IsNullOrWhiteSpace(query) ? author.Name : query!.Trim();
            var candidates = await _upcomingReleaseService.SearchAuthorMatchCandidatesAsync(searchTerm);
            return candidates
                .Select(c => new AuthorMatchCandidateDto(c.SourceId, c.Source, c.Name, c.SourceUrl, c.BookCount))
                .ToList();
        }
        catch (HardcoverDailyLimitExceededException ex)
        {
            // 4xx detail is relayed to the user by design, mirroring SeriesController's refresh
            // action - this message names the cause and the remedy without leaking anything
            // about the environment.
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Hardcover match candidates for author {AuthorId}", authorId);
            return this.UnexpectedError();
        }
    }

    [HttpPost("authors/{authorId}/hardcover-match")]
    public async Task<IActionResult> MatchAuthor(long authorId, [FromBody] MatchAuthorDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.SourceId) || string.IsNullOrWhiteSpace(dto.SourceName))
        {
            return this.InvalidRequest("SourceId and SourceName are required.");
        }

        // The roster refresh this match immediately triggers shares the SAME
        // <see cref="IExpectedBookWriteGate"/> the single-author refresh and the bulk sweep take,
        // so a match can never race them - or a series-side roster write - into a concurrent
        // upsert/prune of the same unified rows. A busy gate refuses with 409, exactly like the
        // explicit refresh endpoints.
        if (!_expectedBookWriteGate.TryAcquire())
        {
            return this.ConflictingState("An author-roster refresh is already in progress.", "Operation in progress");
        }

        try
        {
            await _upcomingReleaseService.MatchAuthorAsync(authorId, dto.SourceId, dto.SourceName, dto.SourceUrl);

            // Persist-first: the match is stored BEFORE the refresh, so a transient refresh
            // failure (e.g. the source's daily budget) surfaces as this call's error while the
            // match stays stored - the periodic sweep picks the roster up on its next tick.
            await _upcomingReleaseService.RefreshAuthorRosterAsync(authorId);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (HardcoverDailyLimitExceededException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error matching author {AuthorId} to Hardcover author {SourceId}", authorId, dto.SourceId);
            return this.UnexpectedError();
        }
        finally
        {
            _expectedBookWriteGate.Release();
        }
    }

    [HttpDelete("authors/{authorId}/hardcover-match")]
    public async Task<IActionResult> UnmatchAuthor(long authorId)
    {
        try
        {
            await _upcomingReleaseService.UnmatchAuthorAsync(authorId);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing the Hardcover match for author {AuthorId}", authorId);
            return this.UnexpectedError();
        }
    }

    [HttpGet("audiobooks/{id}")]
    public async Task<ActionResult<AudiobookDetailDto>> GetAudiobookDetail(long id)
    {
        var audiobook = await _audiobookRepo.GetByIdWithIncludesAsync(id);
        if (audiobook == null)
        {
            return NotFound();
        }

        return new AudiobookDetailDto(
            audiobook.Id,
            audiobook.BookName,
            audiobook.Subtitle,
            audiobook.Series,
            audiobook.SeriesPart,
            audiobook.Year,
            audiobook.Authors.Select(p => p.Name).ToList(),
            audiobook.Narrators.Select(p => p.Name).ToList(),
            audiobook.Genres.Select(g => g.Name).ToList(),
            audiobook.Description,
            audiobook.Copyright,
            audiobook.Publisher,
            audiobook.Language,
            audiobook.Rating,
            audiobook.Asin,
            audiobook.Www,
            audiobook.CoverFilePath,
            audiobook.DurationInSeconds,
            audiobook.FileInfoFullPath,
            audiobook.FileInfoFileName,
            audiobook.FileInfoSizeInBytes,
            audiobook.LastMetadataRefreshedAt,
            audiobook.Authors
                .Select(a => new AudiobookAuthorDto(a.Id, a.Name))
                .ToList()
        );
    }

    [HttpGet("audiobooks/{id}/cover")]
    public async Task<IActionResult> GetAudiobookCover(long id)
    {
        var coverFilePath = await _audiobookRepo.GetCoverFilePathAsync(id);
        if (string.IsNullOrEmpty(coverFilePath) || !System.IO.File.Exists(coverFilePath))
            return NotFound();

        var mimeType = coverFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        // Stream the file instead of buffering it, and let the browser revalidate rather than
        // re-download: a 50-row library page requests 50 of these. Deliberately no max-age -
        // a cover is rewritten in place whenever the book is saved, and nothing in the URL
        // changes when it is, so a freshness window would serve a stale image for its duration.
        // The ETag/Last-Modified pair makes the repeat request a cheap 304 instead.
        var fullPath = Path.GetFullPath(coverFilePath);
        var lastModified = new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        var length = new FileInfo(fullPath).Length;
        var entityTag = new EntityTagHeaderValue($"\"{lastModified.ToUnixTimeMilliseconds():x}-{length:x}\"");

        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(fullPath, mimeType, lastModified, entityTag, enableRangeProcessing: true);
    }

    // The series name is a query parameter, not a path segment - see the note on SeriesController
    // for why a free-text m4b tag cannot be addressed in a path.
    [HttpGet("series")]
    public async Task<List<AudiobookSummaryDto>> GetSeriesBooks([FromQuery] string seriesName, [FromQuery] long? authorId)
    {
        var books = await _audiobookRepo.GetBooksBySeriesAsync(seriesName, authorId);
        return books.Select(MapToSummaryDto).ToList();
    }

    private static AudiobookSummaryDto MapToSummaryDto(Database.Models.Audiobook a)
    {
        return new AudiobookSummaryDto(
            a.Id,
            a.BookName,
            a.Subtitle,
            a.Series,
            a.SeriesPart,
            a.Year,
            a.Authors.Select(p => p.Name).ToList(),
            a.Narrators.Select(p => p.Name).ToList(),
            a.Genres.Select(g => g.Name).ToList(),
            a.CoverFilePath,
            a.DurationInSeconds
        );
    }
}
