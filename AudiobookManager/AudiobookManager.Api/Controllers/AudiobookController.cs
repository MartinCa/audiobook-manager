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
    public async Task<string> OrganizeAudiobook([FromBody] OrganizeAudiobookDto dto)
    {
        var book = MapToDomain(dto);
        var task = await _organizeTaskService.QueueOrganizeTask(book);

        return task.OriginalFileLocation;
    }

    [HttpPost("generate_path")]
    public string GeneratePath([FromBody] OrganizeAudiobookDto dto)
    {
        var book = MapToDomain(dto);
        return _audiobookService.GenerateLibraryPath(book);
    }

    private static Audiobook MapToDomain(OrganizeAudiobookDto dto)
    {
        var authors = dto.Authors.Select(a => new Person(a)).ToList();
        var narrators = dto.Narrators.Select(n => new Person(n)).ToList();
        var fileInfo = new AudiobookFileInfo(dto.FilePath, dto.FileName, dto.SizeInBytes);

        AudiobookImage? cover = null;
        if (dto.Cover is not null)
        {
            cover = new AudiobookImage(dto.Cover.Base64Data, dto.Cover.MimeType);
        }

        return new Audiobook(authors, dto.BookName, dto.Year, fileInfo)
        {
            Narrators = narrators,
            Subtitle = dto.Subtitle,
            Series = dto.Series,
            SeriesPart = dto.SeriesPart,
            Genres = dto.Genres,
            Description = dto.Description,
            Copyright = dto.Copyright,
            Publisher = dto.Publisher,
            Rating = dto.Rating,
            Asin = dto.Asin,
            Www = dto.Www,
            Cover = cover
        };
    }
}
