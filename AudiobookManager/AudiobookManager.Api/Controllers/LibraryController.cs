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
public class LibraryController : ControllerBase
{
    // Scanning and importing retain separate gates so starting one does not unexpectedly reject
    // the other; the two import variants share a gate because they both mutate discovered files.
    // The scan gate is the combined scan-and-consistency gate: the library scan *is* the full
    // consistency check now (one background operation), so ConsistencyController's check endpoints
    // must exclude it and be excluded by it.
    private static readonly SemaphoreSlim _scanLock = BackgroundOperationGates.LibraryScanAndConsistency;
    private static readonly SemaphoreSlim _bulkImportLock = new(1, 1);

    public const string ScanOperationKey = "library-scan";
    public const string BulkImportOperationKey = "discovered-import";

    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IDiscoveredAudiobookRepository _discoveredRepo;
    private readonly ILibraryScanService _libraryScanService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IDiscoveredAudiobookRepository discoveredRepo,
        ILibraryScanService libraryScanService,
        IHostApplicationLifetime appLifetime,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<LibraryController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _discoveredRepo = discoveredRepo;
        _libraryScanService = libraryScanService;
        _appLifetime = appLifetime;
        _settings = settings.Value;
        _logger = logger;
    }

    [HttpPost("scan")]
    public IActionResult StartLibraryScan()
    {
        // Asked here, before the operation is handed to BackgroundOperationRunner, because that
        // path is fire-and-forget: an exception thrown inside the work is logged and reported to
        // the client as zeroed completion events, which reads as "your library is fine" - the
        // opposite of what a missing library means. LibraryScanOrchestrator re-checks this itself
        // (before clearing anything) and is the actual guard; this is what makes the refusal
        // legible.
        if (!SettingsValidation.IsDirectoryUsable(_settings.AudiobookLibraryPath))
        {
            _logger.LogWarning(
                "Refused library scan: library directory '{LibraryPath}' is not available",
                _settings.AudiobookLibraryPath);

            return this.ConflictingState(
                LibraryAvailability.UnavailableMessage(_settings),
                "Library unavailable");
        }

        // Whether the scan's own completion has already been reported (as soon as discovery
        // finishes). On a later error the runner must not clobber it with zeroed counts - the
        // rows discovery committed are real and visible even if the consistency half fails.
        var discoveryCompleted = false;

        return BackgroundOperationRunner.Start(
            _scanLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            ScanOperationKey,
            async sp =>
            {
                // Resolved from the runner's own scope, never the request scope: the operation
                // outlives the request and every service it touches has to see the same database
                // context it was created with. The orchestrator is a leaf that brings in the
                // scan and consistency services behind the shared gate.
                var orchestrator = sp.GetRequiredService<ILibraryScanOrchestrator>();

                Task DiscoveryProgress(string message, int filesScanned, int total)
                {
                    _statusRegistry.SetProgress(ScanOperationKey, filesScanned, total);
                    return _organizeHub.Clients.All.LibraryScanProgress(
                        new LibraryScanProgress(message, filesScanned, total));
                }

                Task ConsistencyProgress(string message, int booksChecked, int totalBooks, int issuesFound)
                {
                    _statusRegistry.SetProgress(ScanOperationKey, booksChecked, totalBooks);
                    return _organizeHub.Clients.All.ConsistencyCheckProgress(
                        new ConsistencyCheckProgress(message, booksChecked, totalBooks, issuesFound, ConsistencyCheckScope.Library));
                }

                var result = await orchestrator.RunCombinedScanAsync(
                    DiscoveryProgress,
                    ConsistencyProgress,
                    (totalFiles, newFiles, alreadyTracked) =>
                    {
                        discoveryCompleted = true;
                        return _organizeHub.Clients.All.LibraryScanComplete(
                            new LibraryScanComplete(totalFiles, newFiles, alreadyTracked));
                    });

                await _organizeHub.Clients.All.ConsistencyCheckComplete(
                    new ConsistencyCheckComplete(result.BooksChecked, result.IssuesFound, ConsistencyCheckScope.Library));
            },
            () =>
            {
                var sends = new List<Task>();
                if (!discoveryCompleted)
                {
                    sends.Add(_organizeHub.Clients.All.LibraryScanComplete(new LibraryScanComplete(0, 0, 0)));
                }
                sends.Add(_organizeHub.Clients.All.ConsistencyCheckComplete(
                    new ConsistencyCheckComplete(0, 0, ConsistencyCheckScope.Library)));
                return Task.WhenAll(sends);
            },
            _appLifetime.ApplicationStopping);
    }

    [HttpGet("discovered")]
    public async Task<DiscoveredAudiobookPageDto> GetDiscovered(int limit = 20, int offset = 0, string? search = null)
    {
        var (items, total) = await _discoveredRepo.GetPaginatedAsync(limit, offset, search);
        // The count is global, not search-filtered, and is used by the unfiltered page to offer
        // the all-books action. Avoid repeating the full-table count for every search keystroke.
        var wellTaggedTotal = string.IsNullOrWhiteSpace(search)
            ? await _discoveredRepo.CountWellTaggedAsync()
            : 0;
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

        return new DiscoveredAudiobookPageDto(mapped.Count, total, wellTaggedTotal, mapped);
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

        return StartBulkImportOperation((scanService, progressAction, onItemFailed) =>
            scanService.BulkImportAsync(dto.Paths, progressAction, onItemFailed));
    }

    [HttpPost("discovered/bulk-import-well-tagged")]
    public IActionResult StartBulkImportWellTagged()
    {
        return StartBulkImportOperation((scanService, progressAction, onItemFailed) =>
            scanService.BulkImportAllWellTaggedAsync(progressAction, onItemFailed));
    }

    private IActionResult StartBulkImportOperation(
        Func<ILibraryScanService, Func<int, int, int, int, Task>, Func<string, string, Task>, Task<(int Processed, int Succeeded, int Failed)>> startImport)
    {
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

                var (processed, succeeded, failed) = await startImport(scanService, ProgressAction, OnItemFailed);

                await _organizeHub.Clients.All.DiscoveredImportComplete(
                    new DiscoveredImportComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.DiscoveredImportComplete(new DiscoveredImportComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }
}
