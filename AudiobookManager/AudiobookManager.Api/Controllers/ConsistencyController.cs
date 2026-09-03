using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
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

    public const string OperationKey = "consistency-check";

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

            return Problem(
                detail:
                    $"The library directory '{_settings.AudiobookLibraryPath}' is not available, so every book "
                    + "would look missing. This is normally a volume mount - check it is mounted and readable "
                    + "by the user this application runs as, then run the check again.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Library unavailable");
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
                        new ConsistencyCheckProgress(message, booksChecked, totalBooks, issuesFound));
                }

                var (booksChecked, issuesFound) = await consistencyService.RunConsistencyCheck(ProgressAction);

                await _organizeHub.Clients.All.ConsistencyCheckComplete(
                    new ConsistencyCheckComplete(booksChecked, issuesFound));
            },
            () => _organizeHub.Clients.All.ConsistencyCheckComplete(new ConsistencyCheckComplete(0, 0)),
            _appLifetime.ApplicationStopping);
    }

    [HttpGet("issues")]
    public async Task<List<ConsistencyIssueDto>> GetIssues()
    {
        var issues = await _issueRepository.GetAllWithAudiobookAsync();
        return issues.Select(i => new ConsistencyIssueDto(
            i.Id,
            i.AudiobookId,
            i.Audiobook.BookName,
            i.Audiobook.Authors.Select(a => a.Name).ToList(),
            i.IssueType.ToString(),
            i.Description,
            i.ExpectedValue,
            i.ActualValue,
            i.DetectedAt
        )).ToList();
    }

    [HttpGet("issues/summary")]
    public async Task<Dictionary<long, int>> GetIssueSummary()
    {
        return await _issueRepository.GetIssueSummaryAsync();
    }

    [HttpGet("issues/by-audiobook/{audiobookId}")]
    public async Task<List<ConsistencyIssueDto>> GetIssuesByAudiobook(long audiobookId)
    {
        var issues = await _issueRepository.GetByAudiobookIdAsync(audiobookId);
        return issues.Select(i => new ConsistencyIssueDto(
            i.Id,
            i.AudiobookId,
            i.Audiobook.BookName,
            i.Audiobook.Authors.Select(a => a.Name).ToList(),
            i.IssueType.ToString(),
            i.Description,
            i.ExpectedValue,
            i.ActualValue,
            i.DetectedAt
        )).ToList();
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
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("issues/resolve-by-type/{issueType}")]
    public async Task<IActionResult> ResolveIssuesByType(string issueType)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var (resolved, failed) = await consistencyService.ResolveIssuesByType(issueType);
            return Ok(new { resolved, failed });
        }
        catch (LibraryUnavailableException ex)
        {
            // Not a server error: the library is not in a state where this sweep can be trusted,
            // and the message says what to check. A 409 so the client shows it rather than a
            // generic failure.
            _logger.LogWarning("Refused bulk resolve of {IssueType}: {Reason}", issueType, ex.Message);

            // ProblemDetails rather than a bare string: the client reads ApiError.message from
            // problem.detail, and a string body serializes as text/plain, which its error parser
            // skips - so the "what to check" message this refusal exists to deliver never reached
            // the toast. Also a drop-in once AddProblemDetails is registered.
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Library unavailable");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk resolving consistency issues of type {IssueType}", issueType);
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("issues/resolve-selected")]
    public async Task<IActionResult> ResolveSelectedIssues([FromBody] List<long> issueIds)
    {
        if (issueIds == null || issueIds.Count == 0)
            return BadRequest("No issue ids provided");

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            var (resolved, failed) = await consistencyService.ResolveIssues(issueIds);
            return Ok(new { resolved, failed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk resolving selected consistency issues");
            return StatusCode(500, ex.Message);
        }
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
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tag mismatch fields for issue {IssueId}", id);
            return StatusCode(500, ex.Message);
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
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve tag mismatch for issue {IssueId}", id);
            return StatusCode(500, ex.Message);
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
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving consistency issue {IssueId}", id);
            return StatusCode(500, ex.Message);
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
            return StatusCode(500, ex.Message);
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
            return StatusCode(500, ex.Message);
        }
    }
}
