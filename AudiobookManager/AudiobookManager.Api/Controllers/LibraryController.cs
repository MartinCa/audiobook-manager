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
                var (totalFiles, newFilesDiscovered, trackedFiles) = await scanService.ScanLibrary(async (message, filesScanned, total) =>
                {
                    await _organizeHub.Clients.All.LibraryScanProgress(
                        new LibraryScanProgress(message, filesScanned, total));
                });

                await _organizeHub.Clients.All.LibraryScanComplete(
                    new LibraryScanComplete(totalFiles, newFilesDiscovered, trackedFiles));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during library scan");
                try
                {
                    await _organizeHub.Clients.All.LibraryScanComplete(
                        new LibraryScanComplete(0, 0, 0));
                }
                catch (Exception hubEx)
                {
                    _logger.LogError(hubEx, "Failed to send LibraryScanComplete over SignalR");
                }
            }
        });

        return Ok();
    }

    [HttpGet("discovered")]
    public async Task<PaginatedResult<AudiobookFileInfo>> GetDiscovered(int limit = 20, int offset = 0)
    {
        var (items, total) = await _discoveredRepo.GetPaginatedAsync(limit, offset);
        var mapped = items
            .Select(d => new AudiobookFileInfo(d.FileInfoFullPath, d.FileInfoFileName, d.FileInfoSizeInBytes))
            .ToList();

        return new PaginatedResult<AudiobookFileInfo>(mapped.Count, total, mapped);
    }

    [HttpDelete("discovered")]
    public async Task<IActionResult> DeleteDiscovered([FromQuery] string path)
    {
        await _discoveredRepo.DeleteByPathAsync(path);
        return NoContent();
    }
}
