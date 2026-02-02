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
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly ILogger<ConsistencyController> _logger;

    public ConsistencyController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IConsistencyIssueRepository issueRepository,
        ILogger<ConsistencyController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    [HttpPost("check")]
    public IActionResult StartConsistencyCheck()
    {
        Task.Run(async () =>
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var consistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();

            try
            {
                var totalBooksChecked = 0;
                var totalIssuesFound = 0;

                await consistencyService.RunConsistencyCheck(async (message, booksChecked, totalBooks, issuesFound) =>
                {
                    totalBooksChecked = booksChecked;
                    totalIssuesFound = issuesFound;
                    await _organizeHub.Clients.All.ConsistencyCheckProgress(
                        new ConsistencyCheckProgress(message, booksChecked, totalBooks, issuesFound));
                });

                await _organizeHub.Clients.All.ConsistencyCheckComplete(
                    new ConsistencyCheckComplete(totalBooksChecked, totalIssuesFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during consistency check");
                try
                {
                    await _organizeHub.Clients.All.ConsistencyCheckComplete(
                        new ConsistencyCheckComplete(0, 0));
                }
                catch (Exception hubEx)
                {
                    _logger.LogError(hubEx, "Failed to send ConsistencyCheckComplete over SignalR");
                }
            }
        });

        return Ok();
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving consistency issue {IssueId}", id);
            return StatusCode(500, ex.Message);
        }
    }
}
