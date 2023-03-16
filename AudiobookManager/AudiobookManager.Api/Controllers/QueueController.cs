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
}
