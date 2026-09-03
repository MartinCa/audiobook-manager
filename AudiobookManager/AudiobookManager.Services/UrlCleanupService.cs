using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Utils;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class UrlCleanupService : IUrlCleanupService
{
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ILogger<UrlCleanupService> _logger;

    public UrlCleanupService(
        IAudiobookRepository audiobookRepository,
        IAudiobookService audiobookService,
        IAudiobookSaveGate saveGate,
        ILogger<UrlCleanupService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _audiobookService = audiobookService;
        _saveGate = saveGate;
        _logger = logger;
    }

    public async Task<List<AudiobookUrlCleanup>> FindDirtyUrlsAsync()
    {
        var audiobooks = await _audiobookRepository.GetAllWithIncludesAsync();

        var results = new List<AudiobookUrlCleanup>();
        foreach (var audiobook in audiobooks)
        {
            if (string.IsNullOrWhiteSpace(audiobook.Www))
            {
                continue;
            }

            var cleaned = BookUrlCleaner.Clean(audiobook.Www);
            if (string.Equals(cleaned, audiobook.Www, StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new AudiobookUrlCleanup(
                audiobook.Id,
                audiobook.BookName,
                audiobook.Authors.Select(p => p.Name).ToList(),
                audiobook.Www,
                cleaned));
        }

        return results;
    }

    public async Task<int> ApplyAsync(IEnumerable<long> audiobookIds)
    {
        var ids = audiobookIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var updated = 0;

        await BulkOperationRunner.RunAsync(
            ids,
            async id =>
            {
                // Cleanup rewrites the m4b's Www tag as well as the DB record (via
                // AudiobookService.UpdateAudiobook, which keeps both in sync - see
                // TagConsistencyChecker), so it takes the same per-audiobook gate an interactive
                // save does. A book someone is editing right now fails just its own item;
                // BulkOperationRunner counts it and the rest of the batch carries on.
                using var lease = _saveGate.Acquire(id);

                var audiobook = await _audiobookService.GetAudiobookById(id);
                if (audiobook is null || string.IsNullOrWhiteSpace(audiobook.Www))
                {
                    return;
                }

                var cleaned = BookUrlCleaner.Clean(audiobook.Www);
                if (string.Equals(cleaned, audiobook.Www, StringComparison.Ordinal))
                {
                    return;
                }

                audiobook.Www = cleaned;
                await _audiobookService.UpdateAudiobook(id, audiobook);
                updated++;
            },
            _logger,
            id => $"Failed to clean URL for audiobook {id}",
            progressAction: null);

        return updated;
    }
}
