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
    private readonly IQueuedOrganizeTaskService _organizeTaskService;

    public AudiobookController(IAudiobookService audiobookService, IQueuedOrganizeTaskService organizeTaskService)
    {
        _audiobookService = audiobookService;
        _organizeTaskService = organizeTaskService;
    }

    [HttpPost("details")]
    public Audiobook ParseAudiobook([FromBody] PathDto dto)
    {
        return _audiobookService.ParseAudiobook(dto.Path);
    }

    [HttpPost("organize")]
    public async Task<string> OrganizeAudiobook([FromBody] Audiobook book)
    {
        var task = await _organizeTaskService.QueueOrganizeTask(book);

        return task.OriginalFileLocation;
    }

    [HttpPost("generate_path")]
    public string GeneratePath([FromBody] Audiobook book)
    {
        return _audiobookService.GenerateLibraryPath(book);
    }
}
