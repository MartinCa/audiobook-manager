using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class UntaggedController : Controller
{
    private readonly IUntaggedService _untaggedService;

    public UntaggedController(IUntaggedService untaggedService)
    {
        this._untaggedService = untaggedService;
    }

    [HttpGet(Name = "GetUntaggedAudiobookFiles")]
    public IEnumerable<AudiobookFileInfo> Index()
    {
        return _untaggedService.ScanInputDirectoryForAudiobookFiles();
    }

    [HttpGet("/test")]
    public void Test()
    {
    }
}
