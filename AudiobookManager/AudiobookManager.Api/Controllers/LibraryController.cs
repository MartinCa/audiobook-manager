using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LibraryController : ControllerBase
{
    private static readonly SemaphoreSlim _scanLock = new(1, 1);
    private static readonly SemaphoreSlim _bulkImportLock = new(1, 1);

    public const string ScanOperationKey = "library-scan";
    public const string BulkImportOperationKey = "discovered-import";

    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IDiscoveredAudiobookRepository _discoveredRepo;
    private readonly ILibraryScanService _libraryScanService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IDiscoveredAudiobookRepository discoveredRepo,
        ILibraryScanService libraryScanService,
        IHostApplicationLifetime appLifetime,
        ILogger<LibraryController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _discoveredRepo = discoveredRepo;
        _libraryScanService = libraryScanService;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpPost("scan")]
    public IActionResult StartLibraryScan()
    {
        return BackgroundOperationRunner.Start(
            _scanLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ScanOperationKey,
            async sp =>
            {
                var scanService = sp.GetRequiredService<ILibraryScanService>();

                Task ProgressAction(string message, int filesScanned, int total)
                {
                    _statusRegistry.SetProgress(ScanOperationKey, filesScanned, total);
                    return _organizeHub.Clients.All.LibraryScanProgress(
                        new LibraryScanProgress(message, filesScanned, total));
                }

                var (totalFiles, newFilesDiscovered, trackedFiles) = await scanService.ScanLibrary(ProgressAction);

                await _organizeHub.Clients.All.LibraryScanComplete(
                    new LibraryScanComplete(totalFiles, newFilesDiscovered, trackedFiles));
            },
            () => _organizeHub.Clients.All.LibraryScanComplete(new LibraryScanComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    [HttpGet("discovered")]
    public async Task<PaginatedResult<DiscoveredAudiobookDto>> GetDiscovered(int limit = 20, int offset = 0, string? search = null)
    {
        var (items, total) = await _discoveredRepo.GetPaginatedAsync(limit, offset, search);
        var mapped = items.Select(item => new DiscoveredAudiobookDto(item)).ToList();

        // Each duplicate check is an independent, synchronous filesystem probe. Run the page's
        // probes in parallel off the request thread rather than wrapping them in Task.WhenAll,
        // which - because the check never awaits - executed them one at a time inline.
        var pairs = items.Zip(mapped, (item, dto) => (item, dto))
            .Where(pair => pair.dto.IsWellTagged)
            .ToList();

        if (pairs.Count > 0)
        {
            await Task.Run(() => Parallel.ForEach(
                pairs,
                pair => pair.dto.IsDuplicate = _libraryScanService.IsDuplicateTarget(pair.item)));
        }

        return new PaginatedResult<DiscoveredAudiobookDto>(mapped.Count, total, mapped);
    }

    [HttpDelete("discovered")]
    public async Task<IActionResult> DeleteDiscovered([FromQuery] string path)
    {
        await _discoveredRepo.DeleteByPathAsync(path);
        return NoContent();
    }

    [HttpPost("discovered/bulk-import")]
    public IActionResult StartBulkImport([FromBody] BulkImportDiscoveredDto dto)
    {
        if (dto.Paths == null || dto.Paths.Count == 0)
            return this.InvalidRequest("No paths provided.");

        return BackgroundOperationRunner.Start(
            _bulkImportLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            BulkImportOperationKey,
            async sp =>
            {
                var scanService = sp.GetRequiredService<ILibraryScanService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(BulkImportOperationKey, processed, total);
                    return _organizeHub.Clients.All.DiscoveredImportProgress(
                        new DiscoveredImportProgress(processed, total, succeeded, failed));
                }

                Task OnItemFailed(string path, string error) =>
                    _organizeHub.Clients.All.QueueError(new QueueError(path, error));

                var (processed, succeeded, failed) = await scanService.BulkImportAsync(dto.Paths, ProgressAction, OnItemFailed);

                await _organizeHub.Clients.All.DiscoveredImportComplete(
                    new DiscoveredImportComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.DiscoveredImportComplete(new DiscoveredImportComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }
}
