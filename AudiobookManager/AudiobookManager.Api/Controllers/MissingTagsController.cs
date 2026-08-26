using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MissingTagsController : ControllerBase
{
    private readonly IMissingTagService _missingTagService;

    public MissingTagsController(IMissingTagService missingTagService)
    {
        _missingTagService = missingTagService;
    }

    [HttpGet("fields")]
    public List<MissingTagFieldDto> GetFields()
    {
        return _missingTagService.GetTaggableFields()
            .Select(f => new MissingTagFieldDto(f.Key, f.Label, f.IsCriticalByDefault))
            .ToList();
    }

    [HttpGet("audiobooks")]
    public async Task<List<AudiobookMissingTagsDto>> GetAudiobooksMissingTags([FromQuery] List<string> fields)
    {
        var results = await _missingTagService.FindAudiobooksMissingTagsAsync(fields);
        return results
            .Select(r => new AudiobookMissingTagsDto(r.AudiobookId, r.BookName, r.Authors, r.MissingFields))
            .ToList();
    }
}
