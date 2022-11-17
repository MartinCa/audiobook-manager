using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UntaggedController : ControllerBase
{
    private readonly IFileService _untaggedService;
    private readonly ILogger _logger;

    public UntaggedController(IFileService untaggedService, ILogger<UntaggedController> logger)
    {
        this._untaggedService = untaggedService;
        this._logger = logger;
    }

    [HttpGet(Name = "GetUntaggedAudiobookFiles")]
    public PaginatedResult<AudiobookFileInfo> Index(int limit = 20, int offset = 0)
    {
        var allItems = _untaggedService.ScanInputDirectoryForAudiobookFiles();
        var items = allItems
            .OrderBy(a => a.FullPath)
            .Skip(offset)
            .Take(limit);

        return new PaginatedResult<AudiobookFileInfo>(items.Count(), allItems.Count(), items.ToList());
    }
}
