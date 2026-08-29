using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

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
    private readonly ILogger<ConsistencyController> _logger;

    public ConsistencyController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IConsistencyIssueRepository issueRepository,
        IOrphanDirectoryRepository orphanDirectoryRepository,
        IHostApplicationLifetime appLifetime,
        ILogger<ConsistencyController> logger)
    {
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

    [HttpPost("issues/{id}/resolve")]
    public async Task<IActionResult> ResolveIssue(long id)
    {
        var issue = await _issueRepository.GetByIdAsync(id);
        if (issue == null)
            return NotFound();

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            await consistencyService.ResolveIssue(id);
            return Ok();
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
    public async Task<IActionResult> ResolveOrphanDirectory(long id)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();
            await consistencyService.ResolveOrphanDirectory(id);
            return Ok();
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
            var (resolved, failed) = await consistencyService.ResolveAllOrphanDirectories();
            return Ok(new { resolved, failed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk resolving orphan directories");
            return StatusCode(500, ex.Message);
        }
    }
}
