using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class AudiobookController : ControllerBase
{
    private readonly IAudiobookService _audiobookService;

    public AudiobookController(IAudiobookService audiobookService)
    {
        _audiobookService = audiobookService;
    }

    [HttpPost("details")]
    public Audiobook ParseAudiobook([FromBody] PathDto dto)
    {
        return _audiobookService.ParseAudiobook(dto.Path);
    }

    [HttpPost("organize")]
    public async Task<Audiobook> OrganizeAudiobook([FromBody] Audiobook book)
    {
        var result = _audiobookService.OrganizeAudiobook(book);

        await _audiobookService.InsertAudiobook(result);

        return result;
    }

    [HttpPost("generate_path")]
    public string GeneratePath([FromBody] Audiobook book)
    {
        return _audiobookService.GenerateLibraryPath(book);
    }
}
