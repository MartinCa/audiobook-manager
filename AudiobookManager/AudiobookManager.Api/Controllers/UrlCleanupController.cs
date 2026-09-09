using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
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
}
