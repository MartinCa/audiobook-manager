using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UrlCleanupController : ControllerBase
{
    private readonly IUrlCleanupService _urlCleanupService;

    public UrlCleanupController(IUrlCleanupService urlCleanupService)
    {
        _urlCleanupService = urlCleanupService;
    }

    [HttpGet("audiobooks")]
    public async Task<List<AudiobookUrlCleanupDto>> GetDirtyUrls()
    {
        var results = await _urlCleanupService.FindDirtyUrlsAsync();
        return results
            .Select(r => new AudiobookUrlCleanupDto(r.AudiobookId, r.BookName, r.Authors, r.CurrentUrl, r.CleanedUrl))
            .ToList();
    }

    [HttpPost("apply")]
    public async Task<ApplyUrlCleanupResultDto> Apply([FromBody] ApplyUrlCleanupDto dto)
    {
        var updated = await _urlCleanupService.ApplyAsync(dto.AudiobookIds);
        return new ApplyUrlCleanupResultDto(updated);
    }
}
