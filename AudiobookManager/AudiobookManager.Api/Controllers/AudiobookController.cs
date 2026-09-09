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
    /// The largest multi-select a bulk action will accept. Beyond this the request stops being a
    /// deliberate selection and is more likely a mis-addressed sweep - and each selected book is
    /// a full tag write plus a consistency recheck, so an unbounded list is a foot-gun.
    /// </summary>
    private const int MaxBulkSelection = 100;

    public const string BulkEditOperationKey = "bulk-edit";

    private static readonly SemaphoreSlim _bulkEditLock = new(1, 1);

    private readonly IAudiobookService _audiobookService;
    private readonly IQueuedOrganizeTaskService _organizeTaskService;
    private readonly ILibraryConsistencyService _libraryConsistencyService;
    private readonly IBulkEditService _bulkEditService;
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ICoverImageProcessor _coverImageProcessor;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<AudiobookController> _logger;

    public AudiobookController(
        IAudiobookService audiobookService,
        IQueuedOrganizeTaskService organizeTaskService,
        ILibraryConsistencyService libraryConsistencyService,
        IBulkEditService bulkEditService,
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IAudiobookSaveGate saveGate,
        ICoverImageProcessor coverImageProcessor,
        IHostApplicationLifetime appLifetime,
        ILogger<AudiobookController> logger)
    {
        _audiobookService = audiobookService;
        _organizeTaskService = organizeTaskService;
        _libraryConsistencyService = libraryConsistencyService;
        _bulkEditService = bulkEditService;
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _saveGate = saveGate;
        _coverImageProcessor = coverImageProcessor;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpPost("details")]
    public Audiobook ParseAudiobook([FromBody] PathDto dto)
    {
        return _audiobookService.ParseAudiobook(dto.Path);
    }

    [HttpPost("organize")]
    public async Task<ActionResult<string>> OrganizeAudiobook([FromBody] OrganizeAudiobookDto dto)
    {
        var book = MapToDomain(dto);

        try
        {
            var task = await _organizeTaskService.QueueOrganizeTask(book, dto.MetadataAppliedFromSearch);
            return Ok(task.OriginalFileLocation);
        }
        catch (OrganizeTaskAlreadyQueuedException ex)
        {
            // Already in the queue is not a failure worth a 500 - the first request did what was
            // asked. 409 with the path so the client can say which file.
            return this.ConflictingState(ex.Message, "Already queued");
        }
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

    /// <summary>
    /// The selected books' current field values, so the client can show exactly what a bulk edit
    /// will touch before anything runs. Read-only - no gate, no background work.
    /// </summary>
    [HttpPost("bulk-edit/preview")]
    public async Task<ActionResult<BulkEditPreviewResponseDto>> GetBulkEditPreview([FromBody] BulkSelectionDto? dto)
    {
        var error = ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        var books = await _bulkEditService.GetPreviewAsync(dto!.AudiobookIds);
        return Ok(new BulkEditPreviewResponseDto { Books = books.Select(ToPreviewItem).ToList() });
    }

    /// <summary>
    /// Applies one set of field changes to every selected book. Fire-and-forget through
    /// <see cref="BackgroundOperationRunner"/> with SignalR progress, the same shape as the
    /// similar-value align and consistency-resolve bulk endpoints. Every field is validated here,
    /// before the runner takes over: inside fire-and-forget work a refusal would reach the client
    /// only as a zeroed completion event.
    /// </summary>
    [HttpPost("bulk-edit")]
    public IActionResult StartBulkEdit([FromBody] BulkEditAudiobooksRequestDto? dto)
    {
        var error = ValidateBulkSelection(dto?.AudiobookIds);
        if (error != null)
        {
            return error;
        }

        var changes = new AudiobookBulkChanges();
        var validationError = MapBulkChanges(dto!, changes);
        if (validationError != null)
        {
            return validationError;
        }

        if (changes.IsEmpty)
        {
            return this.InvalidRequest("Nothing to apply.");
        }

        var audiobookIds = dto!.AudiobookIds;
        var capturedChanges = changes;

        return BackgroundOperationRunner.Start(
            _bulkEditLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            BulkEditOperationKey,
            async sp =>
            {
                var bulkEditService = sp.GetRequiredService<IBulkEditService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(BulkEditOperationKey, processed, total);
                    return _organizeHub.Clients.All.BulkEditProgress(
                        new BulkEditProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed) =
                    await bulkEditService.ApplyAsync(capturedChanges, audiobookIds, ProgressAction);

                await _organizeHub.Clients.All.BulkEditComplete(
                    new BulkEditComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.BulkEditComplete(new BulkEditComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    /// <summary>
    /// Every rejection a bulk request can produce, shared by the preview and the apply endpoints;
    /// a duplicate id was the recurring foot-gun, promising N books and touching fewer.
    /// </summary>
    private ObjectResult? ValidateBulkSelection(IReadOnlyList<long>? audiobookIds)
    {
        if (audiobookIds == null || audiobookIds.Count == 0)
        {
            return this.InvalidRequest("At least one audiobook must be selected.");
        }

        if (audiobookIds.Count > MaxBulkSelection)
        {
            return this.InvalidRequest($"No more than {MaxBulkSelection} audiobooks can be selected at once.");
        }

        if (new HashSet<long>(audiobookIds).Count != audiobookIds.Count)
        {
            return this.InvalidRequest("AudiobookIds must not contain duplicates.");
        }

        return null;
    }

    /// <summary>
    /// Validates each present field change and maps it onto <paramref name="changes"/>, returning
    /// the first problem as an RFC 9457 response or null when everything parsed. The strict
    /// rejections (clear on BookName/Year/Authors, an empty author list) exist so a book can never
    /// be bulk-edited into a state the rest of the app does not permit - no title, no year, no
    /// author. Narrators and Genres, whose emptiness is legitimate, accept an explicit clear
    /// instead of the ambiguous empty "replace" list.
    /// </summary>
    private ObjectResult? MapBulkChanges(BulkEditAudiobooksRequestDto dto, AudiobookBulkChanges changes)
    {
        var error = ParseSingleValueChange(dto.BookName, "BookName", clearForbidden: true, c => changes.BookName = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Subtitle, "Subtitle", clearForbidden: false, c => changes.Subtitle = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Series, "Series", clearForbidden: false, c => changes.Series = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.SeriesPart, "SeriesPart", clearForbidden: false, c => changes.SeriesPart = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Description, "Description", clearForbidden: false, c => changes.Description = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Copyright, "Copyright", clearForbidden: false, c => changes.Copyright = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Publisher, "Publisher", clearForbidden: false, c => changes.Publisher = c);
        if (error != null) return error;
        error = ParseSingleValueChange(
            dto.Language, "Language", clearForbidden: false, c => changes.Language = c,
            value => Languages.Normalize(value) ?? value.Trim());
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Rating, "Rating", clearForbidden: false, c => changes.Rating = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Asin, "Asin", clearForbidden: false, c => changes.Asin = c);
        if (error != null) return error;
        error = ParseSingleValueChange(dto.Www, "Www", clearForbidden: false, c => changes.Www = c);
        if (error != null) return error;

        if (dto.Year != null)
        {
            if (dto.Year.Action != "set" && dto.Year.Action != "clear")
            {
                return this.InvalidRequest("Year action must be 'set' or 'clear'.");
            }

            if (dto.Year.Action == "clear")
            {
                return this.InvalidRequest("Year cannot be cleared in bulk; set a new value instead.");
            }

            if (dto.Year.Value == null || dto.Year.Value <= 0)
            {
                return this.InvalidRequest("Year must be a positive number to set.");
            }

            changes.Year = new BulkIntChange(dto.Year.Value, Clear: false);
        }

        error = ParseMultiValueChange(
            dto.Authors, "Authors", allowClear: false,
            "Replacing authors with an empty list would leave the books without an author.", c => changes.Authors = c);
        if (error != null) return error;
        error = ParseMultiValueChange(
            dto.Narrators, "Narrators", allowClear: true,
            "Replacing narrators with an empty list would remove every narrator.", c => changes.Narrators = c);
        if (error != null) return error;
        error = ParseMultiValueChange(
            dto.Genres, "Genres", allowClear: true,
            "Replacing genres with an empty list would remove every genre.", c => changes.Genres = c);
        if (error != null) return error;

        return null;
    }

    private ObjectResult? ParseSingleValueChange(
        BulkEditSingleValueDto? change,
        string fieldName,
        bool clearForbidden,
        Action<BulkSingleChange?> assign,
        Func<string, string>? transformValue = null)
    {
        if (change == null)
        {
            return null;
        }

        if (change.Action != "set" && change.Action != "clear")
        {
            return this.InvalidRequest($"{fieldName} action must be 'set' or 'clear'.");
        }

        if (change.Action == "clear")
        {
            if (clearForbidden)
            {
                return this.InvalidRequest($"{fieldName} cannot be cleared in bulk; set a new value instead.");
            }

            assign(new BulkSingleChange(null, Clear: true));
            return null;
        }

        if (change.Value == null || string.IsNullOrWhiteSpace(change.Value))
        {
            return this.InvalidRequest($"{fieldName} must have a value to set.");
        }

        var value = change.Value.Trim();
        if (transformValue != null)
        {
            value = transformValue(value);
        }

        assign(new BulkSingleChange(value, Clear: false));
        return null;
    }

    /// <summary>
    /// Validates one multi-value field change. "replace" and "add" carry a non-empty value list;
    /// "clear" is the explicit way to empty the list and is only accepted where an empty list is
    /// legitimate (narrators, genres) - never for authors, which a book must keep at least one of.
    /// </summary>
    private ObjectResult? ParseMultiValueChange(
        BulkEditMultiValueDto? change,
        string fieldName,
        bool allowClear,
        string emptyListMessage,
        Action<BulkMultiChange> assign)
    {
        if (change == null)
        {
            return null;
        }

        if (change.Action != "replace" && change.Action != "add" && change.Action != "clear")
        {
            return this.InvalidRequest($"{fieldName} action must be 'replace', 'add' or 'clear'.");
        }

        if (change.Action == "clear")
        {
            if (!allowClear)
            {
                return this.InvalidRequest(
                    $"{fieldName} cannot be cleared in bulk; a book must keep at least one author.");
            }

            assign(new BulkMultiChange(null, new List<string>(), Clear: true));
            return null;
        }

        // Scrub the same way the save path's CleanNames does: a list of blank strings is not a
        // value, and "replace with nothing" is how an empty field got here in the first place.
        // An empty list with an explicit clear above is the one accepted empty form.
        var values = CleanNames(change.Values);
        if (values.Count == 0)
        {
            return this.InvalidRequest(emptyListMessage);
        }

        assign(new BulkMultiChange(change.Action!, values));
        return null;
    }

    private static BulkEditPreviewItemDto ToPreviewItem(Domain.Audiobook book) => new(
        book.Id!.Value,
        book.BookName,
        book.Subtitle,
        book.Series,
        book.SeriesPart,
        book.Year,
        book.Authors.Select(a => a.Name).ToList(),
        book.Narrators.Select(n => n.Name).ToList(),
        book.Genres.ToList(),
        book.Description,
        book.Copyright,
        book.Publisher,
        book.Language,
        book.Rating,
        book.Asin,
        book.Www);

    [HttpPut("{id}")]
    public IActionResult UpdateAudiobook(long id, [FromBody] OrganizeAudiobookDto dto)
    {
        var book = MapToDomain(dto);

        // Taken here rather than inside the background task so the 409 - and the save-status
        // endpoint below - are exact from the moment this action returns. The lease is handed to
        // the task, which owns releasing it.
        if (!_saveGate.TryAcquire(id, out var saveLease))
        {
            return this.ConflictingState($"A save for audiobook {id} is already in progress.", "Save in progress");
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

                await audiobookService.UpdateAudiobook(id, book, ProgressAction, dto.MetadataAppliedFromSearch);

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
            // Same gate and the same wording as the PUT above: an empty 409 gave the client
            // nothing to render but the status code.
            return this.ConflictingState(
                $"A save for audiobook {id} is already in progress.", "Save in progress");
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
