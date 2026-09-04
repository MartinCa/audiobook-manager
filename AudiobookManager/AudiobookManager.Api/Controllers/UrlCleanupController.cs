using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UrlCleanupController : ControllerBase
{
    /// <summary>The largest page a caller may ask for. Beyond this the response stops being a page.</summary>
    private const int MaxPageSize = 200;

    private const int DefaultPageSize = 50;

    /// <summary>
    /// The furthest into the list a caller may ask to start.
    ///
    /// Bounded for two reasons. The offset is <c>page * pageSize</c>, which overflows a 32-bit int
    /// somewhere past page 10.7 million at the maximum page size - and the negative result does not
    /// fail, it is passed to SKIP, where SQLite reads a negative OFFSET as zero and silently serves
    /// the *first* page as though it were the requested one. Separately, even a valid enormous
    /// offset makes the database count its way there row by row. Twenty thousand default-sized
    /// pages is past any real library and well short of both problems.
    /// </summary>
    private const long MaxPageOffset = 1_000_000;

    private readonly IUrlCleanupService _urlCleanupService;

    public UrlCleanupController(IUrlCleanupService urlCleanupService)
    {
        _urlCleanupService = urlCleanupService;
    }

    [HttpGet("audiobooks")]
    public async Task<ActionResult<UrlCleanupPageDto>> GetDirtyUrls(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        if (page < 0)
        {
            return Problem(
                detail: "page must be zero or greater.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return Problem(
                detail: $"pageSize must be between 1 and {MaxPageSize}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        // Widened before multiplying, so the check sees the real product rather than a wrapped one.
        var skip = (long)page * pageSize;
        if (skip > MaxPageOffset)
        {
            return Problem(
                detail: $"page and pageSize together may not skip more than {MaxPageOffset} URLs.",
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
