using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IFileService fileService, ILogger<FilesController> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [HttpPost("directory_contents")]
    public ActionResult<IList<AudiobookFileInfo>> GetDirectoryContents([FromBody] PathDto dto)
    {
        try
        {
            return Ok(_fileService.GetDirectoryContents(dto.Path));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // path is a query parameter, not a path segment - see the note on SeriesController for why a
    // free-text filesystem path cannot be addressed in a route.
    [HttpGet("cover")]
    public IActionResult GetCover([FromQuery] string path)
    {
        string? coverFilePath;
        try
        {
            coverFilePath = _fileService.GetCoverPath(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (string.IsNullOrEmpty(coverFilePath) || !System.IO.File.Exists(coverFilePath))
            return NotFound();

        var mimeType = coverFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        // Same caching approach as BrowseController.GetAudiobookCover: no max-age since nothing
        // in the URL changes when the sidecar is rewritten, ETag/Last-Modified make a repeat
        // request a cheap 304 instead.
        var fullPath = Path.GetFullPath(coverFilePath);
        var lastModified = new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        var length = new FileInfo(fullPath).Length;
        var entityTag = new EntityTagHeaderValue($"\"{lastModified.ToUnixTimeMilliseconds():x}-{length:x}\"");

        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(fullPath, mimeType, lastModified, entityTag, enableRangeProcessing: true);
    }

    [HttpPost("delete_directory")]
    public IActionResult DeleteDirectory([FromBody] PathDto dto)
    {
        _logger.LogInformation("Delete directory requested for path '{Path}'", dto.Path);
        try
        {
            _fileService.DeleteDirectory(dto.Path);
            return Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
