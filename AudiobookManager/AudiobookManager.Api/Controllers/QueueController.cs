using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class QueueController : ControllerBase
{
    private readonly IQueuedOrganizeTaskService _organizeTaskService;

    public QueueController(IQueuedOrganizeTaskService organizeTaskService)
    {
        _organizeTaskService = organizeTaskService;
    }

    [HttpGet("books")]
    public async Task<IList<string>> Index()
    {
        var queuedTasks = await _organizeTaskService.GetQueuedOrganizeTasks();
        return queuedTasks.Select(x => x.OriginalFileLocation).ToList();
    }

    /// <summary>
    /// Rows that have failed to deserialize at least once - including ones dead-lettered past the
    /// retry threshold (see QueuedOrganizeTaskRepository.MaxDeserializationFailures) - so a user
    /// has somewhere to discover a permanently-stuck organize task and do something about it.
    /// </summary>
    [HttpGet("failed")]
    public async Task<List<FailedOrganizeTaskDto>> GetFailed()
    {
        var rows = await _organizeTaskService.GetFailedQueuedOrganizeTasks();
        return rows.Select(r => new FailedOrganizeTaskDto(
            r.OriginalFileLocation,
            r.QueuedTime,
            r.FailureCount,
            r.LastFailureReason,
            r.LastFailureAt)).ToList();
    }

    // originalFileLocation is a raw file path (free text - see AGENTS.md's "Free-text values are
    // addressed in the query string, never in a path segment"), so it comes in via the query
    // string rather than as a route parameter.
    [HttpDelete("failed")]
    public async Task<IActionResult> DeleteFailed([FromQuery] string originalFileLocation)
    {
        await _organizeTaskService.DeleteQueuedOrganizeTask(originalFileLocation);
        return NoContent();
    }

    [HttpPost("failed/retry")]
    public async Task<IActionResult> RetryFailed([FromQuery] string originalFileLocation)
    {
        var retried = await _organizeTaskService.RetryQueuedOrganizeTask(originalFileLocation);
        if (!retried)
        {
            return NotFound();
        }

        return NoContent();
    }
}
