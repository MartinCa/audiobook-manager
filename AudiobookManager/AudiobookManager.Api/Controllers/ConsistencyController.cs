using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ConsistencyController : ControllerBase
{
    private static readonly SemaphoreSlim _checkLock = new(1, 1);
    private static readonly SemaphoreSlim _resolveLock = new(1, 1);

    public const string OperationKey = "consistency-check";
    public const string ResolveOperationKey = "consistency-resolve";
    public const string CheckSelectedOperationKey = "consistency-check-selected";

    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IOrphanDirectoryRepository _orphanDirectoryRepository;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<ConsistencyController> _logger;

    public ConsistencyController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IConsistencyIssueRepository issueRepository,
        IOrphanDirectoryRepository orphanDirectoryRepository,
        IHostApplicationLifetime appLifetime,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<ConsistencyController> logger)
    {
        _settings = settings.Value;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _issueRepository = issueRepository;
        _orphanDirectoryRepository = orphanDirectoryRepository;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpPost("check")]
    public IActionResult StartConsistencyCheck()
    {
        // Asked here, before the operation is handed to BackgroundOperationRunner, because that
        // path is fire-and-forget: an exception thrown inside the work is logged and reported to
        // the client as ConsistencyCheckComplete(0, 0), which reads as "your library is fine" -
        // the opposite of what a missing library means. LibraryConsistencyService re-checks this
        // itself and is the actual guard; this is what makes the refusal legible.
        if (!SettingsValidation.IsDirectoryUsable(_settings.AudiobookLibraryPath))
        {
            _logger.LogWarning(
                "Refused consistency check: library directory '{LibraryPath}' is not available",
                _settings.AudiobookLibraryPath);

            return this.ConflictingState(
                $"The library directory '{_settings.AudiobookLibraryPath}' is not available, so every book "
                + "would look missing. This is normally a volume mount - check it is mounted and readable "
                + "by the user this application runs as, then run the check again.",
                "Library unavailable");
        }

        return BackgroundOperationRunner.Start(
            _checkLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            OperationKey,
            async sp =>
            {
                var consistencyService = sp.GetRequiredService<ILibraryConsistencyService>();

                Task ProgressAction(string message, int booksChecked, int totalBooks, int issuesFound)
                {
                    _statusRegistry.SetProgress(OperationKey, booksChecked, totalBooks);
                    return _organizeHub.Clients.All.ConsistencyCheckProgress(
                        new ConsistencyCheckProgress(message, booksChecked, totalBooks, issuesFound, ConsistencyCheckScope.Library));
                }

                var (booksChecked, issuesFound) = await consistencyService.RunConsistencyCheck(ProgressAction);

                await _organizeHub.Clients.All.ConsistencyCheckComplete(
                    new ConsistencyCheckComplete(booksChecked, issuesFound, ConsistencyCheckScope.Library));
            },
            () => _organizeHub.Clients.All.ConsistencyCheckComplete(
                new ConsistencyCheckComplete(0, 0, ConsistencyCheckScope.Library)),
            _appLifetime.ApplicationStopping);
    }

    /// <summary>
    /// Re-checks only the explicitly selected books, reusing the full check's progress/complete
    /// events so the client has one consistency-check surface to render. Shares the full check's
    /// <c>_checkLock</c> on purpose (they rewrite the same issue rows and read the same files), and
    /// applies the same library-availability refusal, for the same reason
    /// <see cref="StartConsistencyCheck"/> does: a missing library is the one refusal a user must
    /// see synchronously, not as a zeroed completion event.
    /// </summary>
    [HttpPost("check-selected")]
    public IActionResult StartSelectedConsistencyCheck([FromBody] BulkSelectionDto? dto)
    {
        var error = this.ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        if (!SettingsValidation.IsDirectoryUsable(_settings.AudiobookLibraryPath))
        {
            _logger.LogWarning(
                "Refused selected consistency check: library directory '{LibraryPath}' is not available",
                _settings.AudiobookLibraryPath);

            return this.ConflictingState(
                $"The library directory '{_settings.AudiobookLibraryPath}' is not available, so every book "
                + "would look missing. This is normally a volume mount - check it is mounted and readable "
                + "by the user this application runs as, then run the check again.",
                "Library unavailable");
        }

        var audiobookIds = dto!.AudiobookIds;

        return BackgroundOperationRunner.Start(
            _checkLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            CheckSelectedOperationKey,
            async sp =>
            {
                var consistencyService = sp.GetRequiredService<ILibraryConsistencyService>();

                Task ProgressAction(string message, int booksChecked, int totalBooks, int issuesFound)
                {
                    _statusRegistry.SetProgress(CheckSelectedOperationKey, booksChecked, totalBooks);
                    return _organizeHub.Clients.All.ConsistencyCheckProgress(
                        new ConsistencyCheckProgress(message, booksChecked, totalBooks, issuesFound, ConsistencyCheckScope.Selected));
                }

                var (booksChecked, issuesFound) =
                    await consistencyService.RecheckAudiobooksAsync(audiobookIds, ProgressAction);

                await _organizeHub.Clients.All.ConsistencyCheckComplete(
                    new ConsistencyCheckComplete(booksChecked, issuesFound, ConsistencyCheckScope.Selected));
            },
            () => _organizeHub.Clients.All.ConsistencyCheckComplete(
                new ConsistencyCheckComplete(0, 0, ConsistencyCheckScope.Selected)),
            _appLifetime.ApplicationStopping);
    }

    [HttpGet("issues")]
    public async Task<ActionResult<ConsistencyIssuePageDto>> GetIssues(
        [FromQuery] string? issueType = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize)
    {
        if (page < 0)
        {
            return this.InvalidRequest("page must be zero or greater.");
        }

        if (pageSize < 1 || pageSize > PagingLimits.MaxPageSize)
        {
            return this.InvalidRequest($"pageSize must be between 1 and {PagingLimits.MaxPageSize}.");
        }

        // Widened before multiplying, so the check sees the real product rather than a wrapped one.
        var skip = (long)page * pageSize;
        if (skip > PagingLimits.MaxPageOffset)
        {
            return this.InvalidRequest($"page and pageSize together may not skip more than {PagingLimits.MaxPageOffset} issues.");
        }

        ConsistencyIssueType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(issueType))
        {
            if (!Enum.TryParse<ConsistencyIssueType>(issueType, ignoreCase: true, out var value))
            {
                return this.InvalidRequest($"'{issueType}' is not a known consistency issue type.");
            }

            parsedType = value;
        }

        var (issues, totalCount) = await _issueRepository.GetPageWithAudiobookAsync(
            parsedType, (int)skip, pageSize);

        return Ok(new ConsistencyIssuePageDto(issues.Select(ToDto).ToList(), totalCount));
    }

    /// <summary>
    /// How many issues of each type there are, so the client can render the group headers and size
    /// each group's pager without loading the issues themselves.
    /// </summary>
    [HttpGet("issues/counts-by-type")]
    public async Task<Dictionary<string, int>> GetIssueCountsByType()
    {
        var counts = await _issueRepository.GetCountsByTypeAsync();
        return counts.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value);
    }

    private static ConsistencyIssueDto ToDto(ConsistencyIssue issue) => new(
        issue.Id,
        issue.AudiobookId,
        issue.Audiobook.BookName,
        issue.Audiobook.Authors.Select(a => a.Name).ToList(),
        issue.IssueType.ToString(),
        issue.Description,
        issue.ExpectedValue,
        issue.ActualValue,
        issue.DetectedAt
    );

    [HttpGet("issues/summary")]
    public async Task<Dictionary<long, int>> GetIssueSummary()
    {
        return await _issueRepository.GetIssueSummaryAsync();
    }

    [HttpGet("issues/by-audiobook/{audiobookId}")]
    public async Task<List<ConsistencyIssueDto>> GetIssuesByAudiobook(long audiobookId)
    {
        var issues = await _issueRepository.GetByAudiobookIdAsync(audiobookId);
        return issues.Select(ToDto).ToList();
    }

    [HttpPost("issues/recheck/{audiobookId}")]
    public async Task<IActionResult> RecheckAudiobook(long audiobookId)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            await consistencyService.RecheckAudiobookAsync(audiobookId);

            // RecheckAudiobookAsync persists issues without a populated Audiobook navigation
            // property; reload from the repository (like GetIssuesByAudiobook) so BookName/Authors are available.
            var issues = await _issueRepository.GetByAudiobookIdAsync(audiobookId);
            return Ok(issues.Select(i => new ConsistencyIssueDto(
                i.Id,
                i.AudiobookId,
                i.Audiobook.BookName,
                i.Audiobook.Authors.Select(a => a.Name).ToList(),
                i.IssueType.ToString(),
                i.Description,
                i.ExpectedValue,
                i.ActualValue,
                i.DetectedAt
            )).ToList());
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rechecking consistency for audiobook {AudiobookId}", audiobookId);
            return this.UnexpectedError();
        }
    }

    // Both bulk-resolve endpoints share one gate and one operation key: they rewrite the same
    // files through the same service, so a resolve-by-type must exclude a resolve-selected (and
    // vice versa), not just another of its own kind. The work is fire-and-forget with SignalR
    // progress (ConsistencyResolveProgress/Complete), mirroring the similar-value align flow.
    [HttpPost("issues/resolve-by-type/{issueType}")]
    public async Task<IActionResult> ResolveIssuesByType(string issueType)
    {
        // Validated before the runner takes over: inside fire-and-forget work a refusal would
        // reach the client only as ConsistencyResolveComplete(0, 0, 0), which reads as "nothing
        // to resolve" - the opposite of "the sweep was refused, check your mount".
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            await consistencyService.ValidateResolveByTypeAsync(issueType);
        }
        catch (LibraryUnavailableException ex)
        {
            // Not a server error: the library is not in a state where this sweep can be trusted,
            // and the message says what to check. A 409 so the client shows it rather than a
            // generic failure.
            _logger.LogWarning("Refused bulk resolve of {IssueType}: {Reason}", issueType, ex.Message);
            return this.ConflictingState(ex.Message, "Library unavailable");
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating bulk resolve of consistency issues of type {IssueType}", issueType);
            return this.UnexpectedError();
        }

        return BackgroundOperationRunner.Start(
            _resolveLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ResolveOperationKey,
            async sp =>
            {
                var consistencyService = sp.GetRequiredService<ILibraryConsistencyService>();
                // processed (not resolved + failed) as the completion total: cascade-skipped
                // items counted toward the per-item progress the bar showed, so the complete
                // event has to agree with it rather than report a smaller number.
                var (processed, resolved, failed) = await consistencyService.ResolveIssuesByType(issueType, ResolveProgressAction);
                await _organizeHub.Clients.All.ConsistencyResolveComplete(
                    new ConsistencyResolveComplete(processed, resolved, failed));
            },
            () => _organizeHub.Clients.All.ConsistencyResolveComplete(new ConsistencyResolveComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    [HttpPost("issues/resolve-selected")]
    public IActionResult ResolveSelectedIssues([FromBody] List<long> issueIds)
    {
        if (issueIds == null || issueIds.Count == 0)
            return this.InvalidRequest("No issue ids provided.");

        return BackgroundOperationRunner.Start(
            _resolveLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ResolveOperationKey,
            async sp =>
            {
                var consistencyService = sp.GetRequiredService<ILibraryConsistencyService>();
                // Same as ResolveIssuesByType: processed, so the completion total matches the
                // per-item progress (cascade-skips counted) the bar just showed.
                var (processed, resolved, failed) = await consistencyService.ResolveIssues(issueIds, ResolveProgressAction);
                await _organizeHub.Clients.All.ConsistencyResolveComplete(
                    new ConsistencyResolveComplete(processed, resolved, failed));
            },
            () => _organizeHub.Clients.All.ConsistencyResolveComplete(new ConsistencyResolveComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    private Task ResolveProgressAction(int processed, int total, int succeeded, int failed)
    {
        _statusRegistry.SetProgress(ResolveOperationKey, processed, total);
        return _organizeHub.Clients.All.ConsistencyResolveProgress(
            new ConsistencyResolveProgress(processed, total, succeeded, failed));
    }

    [HttpGet("issues/{id}/tag-mismatch")]
    public async Task<ActionResult<List<TagMismatchFieldDto>>> GetTagMismatchFields(long id)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var fields = await consistencyService.GetTagMismatchFieldsAsync(id);
            return Ok(fields.Select(f => new TagMismatchFieldDto(f.Field, f.LibraryValue, f.FileValue)).ToList());
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tag mismatch fields for issue {IssueId}", id);
            return this.UnexpectedError();
        }
    }

    [HttpPost("issues/{id}/tag-mismatch/resolve")]
    public async Task<ActionResult<ConsistencyResolveResultDto>> ResolveTagMismatch(long id, [FromBody] ResolveTagMismatchRequest request)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var result = await consistencyService.ResolveTagMismatchSelectivelyAsync(id, request.FieldValues);
            return Ok(new ConsistencyResolveResultDto(result.IssueId, result.IssueType.ToString(), result.ActionTaken, result.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AudiobookBusyException ex)
        {
            return this.ConflictingState(ex.Message, "Cannot resolve issue");
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve tag mismatch for issue {IssueId}", id);
            return this.UnexpectedError();
        }
    }

    [HttpPost("issues/{id}/resolve")]
    public async Task<ActionResult<ConsistencyResolveResultDto>> ResolveIssue(long id)
    {
        var issue = await _issueRepository.GetByIdAsync(id);
        if (issue == null)
            return NotFound();

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var result = await consistencyService.ResolveIssue(id);
            return Ok(new ConsistencyResolveResultDto(result.IssueId, result.IssueType.ToString(), result.ActionTaken, result.Message));
        }
        catch (AudiobookBusyException ex)
        {
            // Resolving rewrites the book's files, so it takes the same per-audiobook gate a save
            // does. Another operation holding it is a "try again", not a server error.
            return this.ConflictingState(ex.Message, "Cannot resolve issue");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving consistency issue {IssueId}", id);
            return this.UnexpectedError();
        }
    }

    [HttpGet("orphan-directories")]
    public async Task<List<OrphanDirectoryDto>> GetOrphanDirectories()
    {
        var directories = await _orphanDirectoryRepository.GetAllAsync();
        return directories.Select(d => new OrphanDirectoryDto(d.Id, d.DirectoryPath, d.DetectedAt)).ToList();
    }

    [HttpPost("orphan-directories/{id}/resolve")]
    public async Task<ActionResult<OrphanDirectoryResolveResultDto>> ResolveOrphanDirectory(long id)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var result = await consistencyService.ResolveOrphanDirectory(id);
            return Ok(new OrphanDirectoryResolveResultDto(result.Id, result.DirectoryPath, result.ActionTaken, result.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving orphan directory {OrphanDirectoryId}", id);
            return this.UnexpectedError();
        }
    }

    [HttpPost("orphan-directories/resolve-all")]
    public async Task<IActionResult> ResolveAllOrphanDirectories()
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var (resolved, failed, retained) = await consistencyService.ResolveAllOrphanDirectories();
            return Ok(new { resolved, failed, retained });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk resolving orphan directories");
            return this.UnexpectedError();
        }
    }
}
