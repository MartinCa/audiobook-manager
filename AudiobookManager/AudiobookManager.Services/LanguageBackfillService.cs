using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class LanguageBackfillService : ILanguageBackfillService
{
    /// <summary>Books processed between progress reports, as in <see cref="LibraryScanService"/>.</summary>
    private const int ProgressBroadcastInterval = 25;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly ILogger<LanguageBackfillService> _logger;

    public LanguageBackfillService(
        IAudiobookRepository audiobookRepository,
        IAudiobookTagHandler tagHandler,
        ILogger<LanguageBackfillService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _tagHandler = tagHandler;
        _logger = logger;
    }

    public async Task<LanguageBackfillResult> BackfillFromTagsAsync(Func<string, int, int, Task> progressAction)
    {
        var books = await _audiobookRepository.GetBooksMissingLanguageAsync();
        var total = books.Count;

        _logger.LogInformation("Starting language backfill for {Total} books with no recorded language", total);

        var scanned = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var lastMessage = string.Empty;

        foreach (var book in books)
        {
            scanned++;

            try
            {
                // The backfill only reads one tag, so encoding the embedded cover art (plus a
                // base64 string ~1.4x its size) once per book is pure waste.
                var parsed = _tagHandler.ParseAudiobook(new FileInfo(book.FullPath), includeCoverData: false);
                var code = Languages.Normalize(parsed.Language);

                if (code is null)
                {
                    skipped++;
                    lastMessage = $"No usable language tag: {Path.GetFileName(book.FullPath)}";
                }
                else
                {
                    await _audiobookRepository.UpdateLanguageAsync(book.Id, code);
                    updated++;
                    lastMessage = $"Set {code}: {Path.GetFileName(book.FullPath)}";
                }
            }
            catch (Exception ex)
            {
                // One unreadable or missing file must not abort a library-wide pass - the same
                // per-item tolerance the bulk resolve and similar-value alignment use.
                _logger.LogWarning(ex, "Failed to read the language tag of {FilePath}", book.FullPath);
                failed++;
                lastMessage = $"Error reading: {Path.GetFileName(book.FullPath)}";
            }

            await ReportProgressAsync(progressAction, lastMessage, scanned, total);
        }

        _logger.LogInformation(
            "Language backfill complete. Scanned: {Scanned}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}",
            scanned, updated, skipped, failed);

        return new LanguageBackfillResult(scanned, updated, skipped, failed);
    }

    private static Task ReportProgressAsync(
        Func<string, int, int, Task> progressAction, string message, int scanned, int total)
    {
        if (scanned % ProgressBroadcastInterval != 0 && scanned != total)
        {
            return Task.CompletedTask;
        }

        return progressAction(message, scanned, total);
    }
}
