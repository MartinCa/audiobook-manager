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
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ICoverImageProcessor _coverImageProcessor;
    private readonly ILogger<AudiobookController> _logger;

    public AudiobookController(
        IAudiobookService audiobookService,
        IQueuedOrganizeTaskService organizeTaskService,
        ILibraryConsistencyService libraryConsistencyService,
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IAudiobookSaveGate saveGate,
        ICoverImageProcessor coverImageProcessor,
        ILogger<AudiobookController> logger)
    {
        _audiobookService = audiobookService;
        _organizeTaskService = organizeTaskService;
        _libraryConsistencyService = libraryConsistencyService;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _saveGate = saveGate;
        _coverImageProcessor = coverImageProcessor;
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

        // Taken here rather than inside the background task so the 409 - and the save-status
        // endpoint below - are exact from the moment this action returns. The lease is handed to
        // the task, which owns releasing it.
        if (!_saveGate.TryAcquire(id, out var saveLease))
        {
            return Conflict($"A save for audiobook {id} is already in progress");
        }

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
            finally
            {
                saveLease.Dispose();
            }
        });

        return Ok();
    }

    /// <summary>
    /// Whether a save for this book is still running. Progress/complete events are broadcast
    /// over SignalR, so a client that was disconnected while the save finished never sees the
    /// completion and would otherwise sit disabled forever - the editor re-reads this on
    /// reconnect to recover. Reads the same gate the PUT takes, so there is no second source of
    /// truth to drift - which now also means a consistency resolve or an alignment touching this
    /// book reports as busy, because for the editor's purposes it is.
    /// </summary>
    [HttpGet("{id}/save-status")]
    public AudiobookSaveStatusDto GetSaveStatus(long id) =>
        new(id, _saveGate.IsBusy(id));

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAudiobook(long id)
    {
        _logger.LogInformation("Delete audiobook requested for id {AudiobookId}", id);
        var book = await _audiobookService.GetAudiobookById(id);
        if (book == null)
        {
            return NotFound();
        }

        if (!_saveGate.TryAcquire(id, out var saveLease))
        {
            return Conflict();
        }

        try
        {
            await _audiobookService.DeleteAudiobook(id);
            return Ok();
        }
        finally
        {
            saveLease.Dispose();
        }
    }

    private static List<string> CleanNames(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();

    /// <summary>
    /// Folds an incoming language to its ISO 639-1 code, so a value that reached the client from a
    /// scrape or an old free-text tag ("English", "eng") is stored the same way as one picked from
    /// the select. A value naming a language the library does not manage is kept verbatim rather
    /// than dropped - the strict select cannot produce a new one, but a book already carrying one
    /// must not lose it on an unrelated edit.
    /// </summary>
    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        return Languages.Normalize(language) ?? language.Trim();
    }

    private Audiobook MapToDomain(OrganizeAudiobookDto dto)
    {
        // The client splits free-text author/narrator/genre fields, so blank entries reach us for
        // an empty field. Drop them here rather than persisting Person/Genre rows with no name.
        var authors = CleanNames(dto.Authors).Select(a => new Person(a)).ToList();
        var narrators = CleanNames(dto.Narrators).Select(n => new Person(n)).ToList();
        var fileInfo = new AudiobookFileInfo(dto.FilePath, dto.FileName, dto.SizeInBytes);

        // Every client-supplied cover comes through here - organize, save, and the two path
        // preview endpoints - so this is the one place it has to be checked. Covers read back out
        // of an m4b do not pass through here and are not re-encoded.
        var cover = dto.Cover is null
            ? null
            : _coverImageProcessor.Normalize(dto.Cover.Base64Data, dto.Cover.MimeType);

        return new Audiobook(authors, dto.BookName, dto.Year, fileInfo)
        {
            Narrators = narrators,
            Subtitle = dto.Subtitle,
            Series = dto.Series,
            SeriesPart = dto.SeriesPart,
            Genres = CleanNames(dto.Genres),
            Description = dto.Description,
            Copyright = dto.Copyright,
            Publisher = dto.Publisher,
            Language = NormalizeLanguage(dto.Language),
            Rating = dto.Rating,
            Asin = dto.Asin,
            Www = dto.Www,
            Cover = cover,
            ReplaceExisting = dto.ReplaceExisting
        };
    }
}
