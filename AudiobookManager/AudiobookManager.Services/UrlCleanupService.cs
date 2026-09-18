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

    public async Task<(List<AudiobookUrlCleanup> Items, int Total)> FindDirtyUrlsPageAsync(int page, int pageSize)
    {
        // Projected in SQL rather than loaded whole: GetAllWithIncludesAsync pulls Authors,
        // Narrators, Genres and every column for the whole library just to read Www, Id,
        // BookName and Authors. The page and its total are already the *dirty* subset (see the
        // repository's predicate), so the pager and the rendered rows can never disagree.
        var (rows, total) = await _audiobookRepository.GetDirtyUrlPageAsync(pageSize, page * pageSize);

        // BookUrlCleaner stays the source of truth for the transformation: the SQL predicate is
        // only its cheap mirror, kept in step by the repository-paging test that pages through
        // dirty rows end to end.
        var items = rows
            .Select(row => new AudiobookUrlCleanup(
                row.AudiobookId,
                row.BookName,
                row.Authors,
                row.Www,
                BookUrlCleaner.Clean(row.Www)))
            .ToList();

        return (items, total);
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
            async id => { updated += await CleanAudiobookUrlAsync(id); },
            _logger,
            id => $"Failed to clean URL for audiobook {id}",
            progressAction: null);

        return updated;
    }

    public async Task<(int Processed, int Succeeded, int Failed)> ApplyAllAsync(Func<int, int, int, int, Task>? progressAction)
    {
        // Every dirty book, not just the page the client is looking at. Memory stays bounded per
        // the repository's bounded-list invariant: the rows come back one page at a time against
        // the same total order the page endpoint uses (BookName, Id), only the ids are collected
        // into the batch, and the row objects - book names and author lists included - are
        // discarded as each page is consumed. A Fixed page size keeps each repository call (and
        // its SQL projection) small regardless of how large the dirty set is.
        var ids = new List<long>();
        const int pageSize = 500;
        var offset = 0;
        while (true)
        {
            var (rows, total) = await _audiobookRepository.GetDirtyUrlPageAsync(pageSize, offset);
            if (rows.Count == 0 || offset + rows.Count > total)
            {
                // Exhausted, or the tail page reports a count that no longer matches (rows were
                // cleaned by another tab while paging); either way stop fetching rather than
                // looping on an unchanged page.
                rows.ForEach(row => ids.Add(row.AudiobookId));
                break;
            }

            rows.ForEach(row => ids.Add(row.AudiobookId));
            offset += rows.Count;
        }

        if (ids.Count == 0)
        {
            return (0, 0, 0);
        }

        return await BulkOperationRunner.RunAsync(
            ids,
            async id => { await CleanAudiobookUrlAsync(id); },
            _logger,
            id => $"Failed to clean URL for audiobook {id}",
            progressAction);
    }

    private async Task<int> CleanAudiobookUrlAsync(long id)
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
            return 0;
        }

        var cleaned = BookUrlCleaner.Clean(audiobook.Www);
        if (string.Equals(cleaned, audiobook.Www, StringComparison.Ordinal))
        {
            return 0;
        }

        audiobook.Www = cleaned;
        await _audiobookService.UpdateAudiobook(id, audiobook);
        return 1;
    }
}
