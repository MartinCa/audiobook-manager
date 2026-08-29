using System.Collections.Concurrent;
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
    /// <summary>
    /// The audiobook ids with a save currently in flight. UpdateAudiobook rewrites the m4b tags
    /// and relocates the file, so two concurrent saves for the same book (a double-clicked Save,
    /// or a save racing a similar-value alignment) would both read the same pre-move path: the
    /// first moves the file, and the second then writes tags to a path that no longer exists, or
    /// fails with a spurious "already exists". Every other long-running operation is gated the
    /// same way via BackgroundOperationRunner; this endpoint runs its own work, so it carries its
    /// own gate.
    ///
    /// A set rather than a dictionary of semaphores: the check is non-blocking (a second save is
    /// rejected, never queued), so TryAdd/TryRemove expresses it exactly - and unlike a
    /// per-id semaphore it does not accumulate one entry per book ever saved.
    /// </summary>
    private static readonly ConcurrentDictionary<long, byte> _savesInFlight = new();

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

        if (!_savesInFlight.TryAdd(id, 0))
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
                _savesInFlight.TryRemove(id, out _);
            }
        });

        return Ok();
    }

    /// <summary>
    /// Whether a save for this book is still running. Progress/complete events are broadcast
    /// over SignalR, so a client that was disconnected while the save finished never sees the
    /// completion and would otherwise sit disabled forever - the editor re-reads this on
    /// reconnect to recover. Reads the same in-flight set the PUT gate uses, so there is no
    /// second source of truth to drift.
    /// </summary>
    [HttpGet("{id}/save-status")]
    public AudiobookSaveStatusDto GetSaveStatus(long id) =>
        new(id, _savesInFlight.ContainsKey(id));

    private static List<string> CleanNames(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Select(v => v?.Trim() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();

    private static Audiobook MapToDomain(OrganizeAudiobookDto dto)
    {
        // The client splits free-text author/narrator/genre fields, so blank entries reach us for
        // an empty field. Drop them here rather than persisting Person/Genre rows with no name.
        var authors = CleanNames(dto.Authors).Select(a => new Person(a)).ToList();
        var narrators = CleanNames(dto.Narrators).Select(n => new Person(n)).ToList();
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
            Genres = CleanNames(dto.Genres),
            Description = dto.Description,
            Copyright = dto.Copyright,
            Publisher = dto.Publisher,
            Language = dto.Language,
            Rating = dto.Rating,
            Asin = dto.Asin,
            Www = dto.Www,
            Cover = cover
        };
    }
}
