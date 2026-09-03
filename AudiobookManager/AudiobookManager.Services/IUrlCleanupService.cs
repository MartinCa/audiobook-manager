namespace AudiobookManager.Services;

public record AudiobookUrlCleanup(long AudiobookId, string BookName, List<string> Authors, string CurrentUrl, string CleanedUrl);

public interface IUrlCleanupService
{
    Task<List<AudiobookUrlCleanup>> FindDirtyUrlsAsync();

    Task<int> ApplyAsync(IEnumerable<long> audiobookIds);
}
