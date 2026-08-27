using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

/// <summary>
/// Lets the frontend recover the current state of an in-flight or just-finished background
/// operation - on initial mount, or after a SignalR reconnect - instead of relying purely on
/// progress/complete events that may have been missed while disconnected.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class OperationsController : ControllerBase
{
    private readonly IOperationStatusRegistry _registry;

    public OperationsController(IOperationStatusRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet("{key}/status")]
    public OperationStatusDto GetStatus(string key)
    {
        var status = _registry.GetStatus(key);
        return new OperationStatusDto(status.IsRunning, status.Processed, status.Total);
    }
}
