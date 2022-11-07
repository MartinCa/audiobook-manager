using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    [HttpPost("directory_contents")]
    public IList<AudiobookFileInfo> GetDirectoryContents([FromBody] PathDto dto)
    {
        return _fileService.GetDirectoryContents(dto.Path);
    }
}
