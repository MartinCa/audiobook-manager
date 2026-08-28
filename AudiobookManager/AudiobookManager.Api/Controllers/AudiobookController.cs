using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class AudiobookController : ControllerBase
{
    private readonly IAudiobookService _audiobookService;
    private readonly IQueuedOrganizeTaskService _organizeTaskService;
    private readonly ILibraryConsistencyService _libraryConsistencyService;
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AudiobookController> _logger;

    public AudiobookController(
        IAudiobookService audiobookService,
        IQueuedOrganizeTaskService organizeTaskService,
        ILibraryConsistencyService libraryConsistencyService,
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AudiobookController> logger)
    {
        _audiobookService = audiobookService;
        _organizeTaskService = organizeTaskService;
        _libraryConsistencyService = libraryConsistencyService;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
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

    [HttpPost("check_target_path")]
    public async Task<TargetPathCheckDto> CheckTargetPath([FromBody] OrganizeAudiobookDto dto)
    {
        var book = MapToDomain(dto);
        var result = await _audiobookService.CheckTargetPathCollision(book);
        return new TargetPathCheckDto(result);
    }

    [HttpPut("{id}")]
    public IActionResult UpdateAudiobook(long id, [FromBody] OrganizeAudiobookDto dto)
    {
        var book = MapToDomain(dto);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var audiobookService = scope.ServiceProvider.GetRequiredService<IAudiobookService>();
                var libraryConsistencyService = scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>();

                Task ProgressAction(string message, int progress) =>
                    _organizeHub.Clients.All.AudiobookSaveProgress(new AudiobookSaveProgress(id, message, progress));

                await audiobookService.UpdateAudiobook(id, book, ProgressAction);

                try
                {
                    await libraryConsistencyService.RecheckAudiobookAsync(id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after save", id);
                }

                await _organizeHub.Clients.All.AudiobookSaveComplete(new AudiobookSaveComplete(id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating audiobook {AudiobookId}", id);
                try
                {
                    await _organizeHub.Clients.All.AudiobookSaveError(new AudiobookSaveError(id, ex.Message));
                }
                catch (Exception hubEx)
                {
                    _logger.LogError(hubEx, "Failed to send save-error notification over SignalR for audiobook {AudiobookId}", id);
                }
            }
        });

        return Ok();
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
