using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainAudiobookFileInfo = AudiobookManager.Domain.AudiobookFileInfo;

namespace AudiobookManager.Services;

public class LibraryScanService : ILibraryScanService
{
    /// <summary>Discovered rows accumulated before a single batched insert.</summary>
    private const int InsertBatchSize = 200;

    /// <summary>Files scanned between SignalR progress broadcasts.</summary>
    private const int ProgressBroadcastInterval = 25;

    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IDiscoveredAudiobookRepository _discoveredAudiobookRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookService _audiobookService;
    private readonly ILogger<LibraryScanService> _logger;

    public LibraryScanService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IDiscoveredAudiobookRepository discoveredAudiobookRepository,
        IAudiobookTagHandler tagHandler,
        IAudiobookService audiobookService,
        ILogger<LibraryScanService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _discoveredAudiobookRepository = discoveredAudiobookRepository;
        _tagHandler = tagHandler;
        _audiobookService = audiobookService;
        _logger = logger;
    }

    public async Task<(int TotalFiles, int NewFiles, int TrackedFiles)> ScanLibrary(Func<string, int, int, Task> progressAction)
    {
        _logger.LogInformation("Starting library scan of {LibraryPath}", _settings.AudiobookLibraryPath);

        await _discoveredAudiobookRepository.ClearAllAsync();

        var files = FileScanner.ScanDirectoryForFiles(
            _settings.AudiobookLibraryPath,
            AudiobookTagHandler.IsSupported);

        // Paths must be matched the way the file system matches them: a case-only difference is
        // the same file on Windows/macOS, and treating it as new would re-discover (and let the
        // user re-import) a book that is already tracked.
        var knownPaths = await _audiobookRepository.GetAllFilePathsAsync(AudiobookFileHandler.PathComparer);

        var totalFiles = files.Count;
        var filesScanned = 0;
        var newFilesDiscovered = 0;

        // Batch both the writes and the progress reports, the same way LibraryConsistencyService
        // does. Previously this was one transaction *and* one SignalR broadcast per file: a
        // 20,000-book library meant 20,000 of each, and the client throttles the progress bar to
        // ~4fps regardless, so almost all of those messages were invisible.
        var pending = new List<DiscoveredAudiobook>(InsertBatchSize);
        var lastMessage = string.Empty;

        foreach (var file in files)
        {
            filesScanned++;

            if (knownPaths.Contains(file.FullPath))
            {
                lastMessage = $"Already tracked: {file.FileName}";
                await ReportScanProgressAsync(progressAction, lastMessage, filesScanned, totalFiles);
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(file.FullPath);
                // The discovered row stores no cover, so don't pay to encode one per file.
                var parsed = _tagHandler.ParseAudiobook(fileInfo, includeCoverData: false);

                var discovered = new DiscoveredAudiobook(
                    parsed.BookName ?? file.FileName,
                    file.FullPath,
                    file.FileName,
                    file.SizeInBytes,
                    DateTime.UtcNow)
                {
                    Subtitle = parsed.Subtitle,
                    Series = parsed.Series,
                    SeriesPart = parsed.SeriesPart,
                    Year = parsed.Year,
                    Authors = parsed.Authors.Count > 0 ? string.Join(", ", parsed.Authors.Select(a => a.Name)) : null,
                    Narrators = parsed.Narrators.Count > 0 ? string.Join(", ", parsed.Narrators.Select(n => n.Name)) : null,
                    Genres = parsed.Genres.Count > 0 ? string.Join(", ", parsed.Genres) : null,
                    Description = parsed.Description,
                    Copyright = parsed.Copyright,
                    Publisher = parsed.Publisher,
                    Rating = parsed.Rating,
                    Asin = parsed.Asin,
                    Www = parsed.Www,
                    DurationInSeconds = parsed.DurationInSeconds
                };

                pending.Add(discovered);
                lastMessage = $"Discovered: {file.FileName}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse audiobook at {FilePath}", file.FullPath);
                lastMessage = $"Error parsing: {file.FileName}";
            }

            // Deliberately outside the per-file try: a batch insert failing is a database
            // problem, not a parse failure of whichever file happened to fill the batch, and
            // logging it as one blames a file that parsed fine. Flushing here also guarantees
            // `pending` is cleared either way - left in place, a failing batch was re-attempted
            // by every later flush and poisoned the rest of the scan.
            if (pending.Count >= InsertBatchSize)
            {
                newFilesDiscovered += await FlushPendingAsync(pending);
            }

            await ReportScanProgressAsync(progressAction, lastMessage, filesScanned, totalFiles);
        }

        newFilesDiscovered += await FlushPendingAsync(pending);

        _logger.LogInformation("Library scan complete. Total: {Total}, New: {New}, Tracked: {Tracked}",
            totalFiles, newFilesDiscovered, totalFiles - newFilesDiscovered);

        return (totalFiles, newFilesDiscovered, totalFiles - newFilesDiscovered);
    }

    /// <summary>
    /// Writes the accumulated batch and empties it, returning how many rows were actually
    /// persisted. A failure loses that batch - the alternative is aborting a scan of the whole
    /// library over one bad write - so it is logged loudly and the discovered count reflects
    /// only what really landed, rather than what was queued.
    /// </summary>
    private async Task<int> FlushPendingAsync(List<DiscoveredAudiobook> pending)
    {
        if (pending.Count == 0)
        {
            return 0;
        }

        // Hand over a snapshot, not the live buffer: this method clears `pending` in its finally,
        // and anything the repository (or a test double) still holds a reference to would be
        // emptied underneath it.
        var batch = pending.ToList();

        try
        {
            await _discoveredAudiobookRepository.InsertRangeAsync(batch);
            return batch.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to insert a batch of {Count} discovered audiobooks; they will not appear until the next scan", batch.Count);
            return 0;
        }
        finally
        {
            pending.Clear();
        }
    }

    private static Task ReportScanProgressAsync(
        Func<string, int, int, Task> progressAction, string message, int filesScanned, int totalFiles)
    {
        if (filesScanned % ProgressBroadcastInterval != 0 && filesScanned != totalFiles)
        {
            return Task.CompletedTask;
        }

        return progressAction(message, filesScanned, totalFiles);
    }

    public async Task<(int Processed, int Succeeded, int Failed)> BulkImportAsync(
        List<string> filePaths,
        Func<int, int, int, int, Task> progressAction,
        Func<string, string, Task>? onItemFailed = null)
    {
        var discovered = await _discoveredAudiobookRepository.GetByPathsAsync(filePaths);
        var byPath = discovered.ToDictionary(d => d.FileInfoFullPath);

        return await BulkOperationRunner.RunAsync(
            filePaths,
            async path =>
            {
                try
                {
                    if (!byPath.TryGetValue(path, out var entry))
                    {
                        throw new InvalidOperationException($"Discovered audiobook not found for path {path}");
                    }

                    var domain = ToDomainAudiobook(entry);
                    await _audiobookService.OrganizeAudiobook(domain, (_, __) => Task.CompletedTask);
                    await _discoveredAudiobookRepository.DeleteAsync(entry.Id);
                }
                catch (Exception ex) when (onItemFailed is not null)
                {
                    await onItemFailed(path, ex.Message);
                    throw;
                }
            },
            _logger,
            path => $"Failed to bulk import discovered audiobook at {path}",
            progressAction);
    }

    /// <summary>
    /// Whether the discovered file's computed target path is already occupied by a *different*
    /// file. Deliberately synchronous: the work is a path computation plus one File.Exists, and
    /// declaring it async only misled callers into wrapping it in Task.WhenAll for a concurrency
    /// it could never provide.
    /// </summary>
    public bool IsDuplicateTarget(DiscoveredAudiobook entry)
    {
        try
        {
            var domain = ToDomainAudiobook(entry);
            var targetPath = _audiobookService.GenerateLibraryPath(domain);
            if (!File.Exists(targetPath))
            {
                return false;
            }

            // The discovered file may already sit at its own computed target path (e.g. it was
            // scanned in place inside the library). That's not a collision with a different
            // file - it's the same file - so it must not be flagged as a duplicate.
            return !AudiobookFileHandler.PathsEqual(targetPath, entry.FileInfoFullPath);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Builds the domain object from the tag snapshot captured at scan time, rather than
    // re-parsing the file, so the imported book matches exactly what the user reviewed and
    // accepted in the discovered-books list.
    private static DomainAudiobook ToDomainAudiobook(DiscoveredAudiobook entry)
    {
        var authors = AudiobookTagHandler.ParsePersonsFromString(entry.Authors ?? string.Empty);
        if (authors.Count == 0 || string.IsNullOrWhiteSpace(entry.BookName) || !entry.Year.HasValue)
        {
            throw new InvalidOperationException("Missing required tags: author, book name, and year are all required");
        }

        var genres = string.IsNullOrWhiteSpace(entry.Genres)
            ? new List<string>()
            : entry.Genres.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return new DomainAudiobook(
            authors,
            entry.BookName,
            entry.Year,
            new DomainAudiobookFileInfo(entry.FileInfoFullPath, entry.FileInfoFileName, entry.FileInfoSizeInBytes))
        {
            Narrators = AudiobookTagHandler.ParsePersonsFromString(entry.Narrators ?? string.Empty),
            Subtitle = entry.Subtitle,
            Series = entry.Series,
            SeriesPart = entry.SeriesPart,
            Genres = genres,
            Description = entry.Description,
            Copyright = entry.Copyright,
            Publisher = entry.Publisher,
            Rating = entry.Rating,
            Asin = entry.Asin,
            Www = entry.Www,
            DurationInSeconds = entry.DurationInSeconds
        };
    }
}
