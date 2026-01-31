using AudiobookManager.Api.Async;
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
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<LibraryController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
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
}
