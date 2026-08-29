using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MissingTagsController : ControllerBase
{
    private static readonly SemaphoreSlim _backfillLock = new(1, 1);

    public const string LanguageBackfillOperationKey = "language-backfill";

    private readonly IMissingTagService _missingTagService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<MissingTagsController> _logger;

    public MissingTagsController(
        IMissingTagService missingTagService,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        IHostApplicationLifetime appLifetime,
        ILogger<MissingTagsController> logger)
    {
        _missingTagService = missingTagService;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _appLifetime = appLifetime;
        _logger = logger;
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

    /// <summary>
    /// Fills in the language of books that have none from the tag already embedded in their m4b.
    ///
    /// Fire-and-forget: reading every untagged book's file header is a minutes-long pass on a
    /// large library. Unlike the other long-running operations this one publishes no SignalR
    /// event - the client follows it by polling <c>GET api/operations/{key}/status</c>, which
    /// <see cref="BackgroundOperationRunner"/> already keeps up to date, so a one-shot maintenance
    /// job costs no additions to the hub contract.
    /// </summary>
    [HttpPost("backfill-language")]
    public IActionResult StartLanguageBackfill()
    {
        return BackgroundOperationRunner.Start(
            _backfillLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            LanguageBackfillOperationKey,
            async sp =>
            {
                var backfillService = sp.GetRequiredService<ILanguageBackfillService>();

                Task ProgressAction(string message, int scanned, int total)
                {
                    _statusRegistry.SetProgress(LanguageBackfillOperationKey, scanned, total);
                    return Task.CompletedTask;
                }

                var result = await backfillService.BackfillFromTagsAsync(ProgressAction);

                _logger.LogInformation(
                    "Language backfill finished. Scanned: {Scanned}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
                    result.Scanned, result.Updated, result.Skipped, result.Failed);
            },
            () => Task.CompletedTask,
            _appLifetime.ApplicationStopping);
    }
}
