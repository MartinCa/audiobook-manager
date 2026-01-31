using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LibraryController : ControllerBase
{
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IDiscoveredAudiobookRepository _discoveredRepo;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IDiscoveredAudiobookRepository discoveredRepo,
        ILogger<LibraryController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _discoveredRepo = discoveredRepo;
        _logger = logger;
    }

    [HttpPost("scan")]
    public IActionResult StartLibraryScan()
    {
        Task.Run(async () =>
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var scanService = scope.ServiceProvider.GetRequiredService<ILibraryScanService>();

            try
            {
                var totalFiles = 0;
                var newFilesDiscovered = 0;

                await scanService.ScanLibrary(async (message, filesScanned, total) =>
                {
                    totalFiles = total;
                    await _organizeHub.Clients.All.LibraryScanProgress(
                        new LibraryScanProgress(message, filesScanned, total));
                });

                var discoveredRepo = scope.ServiceProvider.GetRequiredService<Database.Repositories.IDiscoveredAudiobookRepository>();
                var discovered = await discoveredRepo.GetAllAsync();
                newFilesDiscovered = discovered.Count;

                await _organizeHub.Clients.All.LibraryScanComplete(
                    new LibraryScanComplete(totalFiles, newFilesDiscovered, totalFiles - newFilesDiscovered));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during library scan");
                await _organizeHub.Clients.All.LibraryScanComplete(
                    new LibraryScanComplete(0, 0, 0));
            }
        });

        return Ok();
    }

    [HttpGet("discovered")]
    public async Task<PaginatedResult<AudiobookFileInfo>> GetDiscovered(int limit = 20, int offset = 0)
    {
        var all = await _discoveredRepo.GetAllAsync();
        var ordered = all.OrderBy(d => d.FileInfoFullPath).ToList();
        var items = ordered
            .Skip(offset)
            .Take(limit)
            .Select(d => new AudiobookFileInfo(d.FileInfoFullPath, d.FileInfoFileName, d.FileInfoSizeInBytes))
            .ToList();

        return new PaginatedResult<AudiobookFileInfo>(items.Count, ordered.Count, items);
    }

    [HttpDelete("discovered")]
    public async Task<IActionResult> DeleteDiscovered([FromQuery] string path)
    {
        await _discoveredRepo.DeleteByPathAsync(path);
        return NoContent();
    }
}
