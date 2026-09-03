using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Services;

public class UrlCleanupService : IUrlCleanupService
{
    private readonly IAudiobookRepository _audiobookRepository;

    public UrlCleanupService(IAudiobookRepository audiobookRepository)
    {
        _audiobookRepository = audiobookRepository;
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
        var idSet = audiobookIds.ToHashSet();
        if (idSet.Count == 0)
        {
            return 0;
        }

        var dirtyUrls = await FindDirtyUrlsAsync();

        var applied = 0;
        foreach (var dirtyUrl in dirtyUrls.Where(d => idSet.Contains(d.AudiobookId)))
        {
            await _audiobookRepository.UpdateWwwAsync(dirtyUrl.AudiobookId, dirtyUrl.CleanedUrl);
            applied++;
        }

        return applied;
    }
}
