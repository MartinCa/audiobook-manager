using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UrlCleanupController : ControllerBase
{
    // Apply-all rewrites the same books the per-selection apply does (through
    // AudiobookService.UpdateAudiobook), so it shares the per-audiobook gate with a save; the
    // lock here only keeps two apply-all sweeps from running concurrently.
    private static readonly SemaphoreSlim _applyAllLock = new(1, 1);

    public const string ApplyAllOperationKey = "url-cleanup-apply";

    private readonly IUrlCleanupService _urlCleanupService;
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<UrlCleanupController> _logger;

    public UrlCleanupController(
        IUrlCleanupService urlCleanupService,
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IHostApplicationLifetime appLifetime,
        ILogger<UrlCleanupController> logger)
    {
        _urlCleanupService = urlCleanupService;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpGet("audiobooks")]
    public async Task<ActionResult<UrlCleanupPageDto>> GetDirtyUrls(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize)
    {
        if (page < 0)
        {
            return Problem(
                detail: "page must be zero or greater.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        if (pageSize < 1 || pageSize > PagingLimits.MaxPageSize)
        {
            return Problem(
                detail: $"pageSize must be between 1 and {PagingLimits.MaxPageSize}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        // Widened before multiplying, so the check sees the real product rather than a wrapped one.
        var skip = (long)page * pageSize;
        if (skip > PagingLimits.MaxPageOffset)
        {
            return Problem(
                detail: $"page and pageSize together may not skip more than {PagingLimits.MaxPageOffset} URLs.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        var (results, totalCount) = await _urlCleanupService.FindDirtyUrlsPageAsync(page, pageSize);

        return Ok(new UrlCleanupPageDto(
            results
                .Select(r => new AudiobookUrlCleanupDto(r.AudiobookId, r.BookName, r.Authors, r.CurrentUrl, r.CleanedUrl))
                .ToList(),
            totalCount));
    }

    [HttpPost("apply")]
    public async Task<ApplyUrlCleanupResultDto> Apply([FromBody] ApplyUrlCleanupDto dto)
    {
        var updated = await _urlCleanupService.ApplyAsync(dto.AudiobookIds);
        return new ApplyUrlCleanupResultDto(updated);
    }

    /// <summary>
    /// Cleans every currently-dirty book in the background, not just the page the client is
    /// looking at. Fire-and-forget with SignalR progress (UrlCleanupProgress/Complete, mirroring
    /// the consistency-resolve flow), so the client closes its flow and renders the bar.
    /// </summary>
    [HttpPost("apply-all")]
    public IActionResult ApplyAll()
    {
        return BackgroundOperationRunner.Start(
            _applyAllLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ApplyAllOperationKey,
            async sp =>
            {
                var urlCleanupService = sp.GetRequiredService<IUrlCleanupService>();

                var (processed, succeeded, failed) = await urlCleanupService.ApplyAllAsync(ProgressAction);
                await _organizeHub.Clients.All.UrlCleanupComplete(
                    new UrlCleanupComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.UrlCleanupComplete(new UrlCleanupComplete(0, 0, 0, errored: true)),
            _appLifetime.ApplicationStopping);
    }

    private Task ProgressAction(int processed, int total, int succeeded, int failed)
    {
        _statusRegistry.SetProgress(ApplyAllOperationKey, processed, total);
        return _organizeHub.Clients.All.UrlCleanupProgress(
            new UrlCleanupProgress(processed, total, succeeded, failed));
    }
}
