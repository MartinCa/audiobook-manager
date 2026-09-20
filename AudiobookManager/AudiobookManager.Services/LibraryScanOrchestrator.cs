using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

/// <summary>
/// The combined scan's results, in the order the two completion events want them: the scan's
/// file totals first, then the consistency check's book/issue totals.
/// </summary>
public sealed record CombinedScanResult(
    int TotalFiles,
    int NewFiles,
    int AlreadyTracked,
    int BooksChecked,
    int IssuesFound);

/// <summary>
/// The single operation behind <c>POST api/library/scan</c>: discovery and consistency in one
/// run, so the library is walked once and the tracked graph is loaded once instead of once per
/// operation. A leaf in the service graph - nothing depends on it, so it intentionally depends
/// on the wide services (<see cref="ILibraryScanService"/>, <see cref="ILibraryConsistencyService"/>)
/// their own graphs already have to support.
/// </summary>
public class LibraryScanOrchestrator : ILibraryScanOrchestrator
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IDiscoveredAudiobookRepository _discoveredAudiobookRepository;
    private readonly ILibraryTreeWalker _treeWalker;
    private readonly ILibraryScanService _scanService;
    private readonly ILibraryConsistencyService _consistencyService;

    public LibraryScanOrchestrator(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IDiscoveredAudiobookRepository discoveredAudiobookRepository,
        ILibraryTreeWalker treeWalker,
        ILibraryScanService scanService,
        ILibraryConsistencyService consistencyService)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _discoveredAudiobookRepository = discoveredAudiobookRepository;
        _treeWalker = treeWalker;
        _scanService = scanService;
        _consistencyService = consistencyService;
    }

    public async Task<CombinedScanResult> RunCombinedScanAsync(
        Func<string, int, int, Task> discoveryProgress,
        Func<string, int, int, int, Task> consistencyProgress,
        Func<int, int, int, Task>? discoveryCompleted = null)
    {
        // Before anything is cleared: a vanished mount makes every file look like a new
        // discovery (and, in the consistency half, every book look deleted). Refusing here
        // protects the previously discovered rows and the previous run's findings.
        LibraryAvailability.EnsureUsable(_settings);

        await _discoveredAudiobookRepository.ClearAllAsync();

        // The tracked graph is loaded once and serves both halves: it derives the scan's
        // known-path set, and it is exactly what the consistency check detects against.
        var audiobooks = await _audiobookRepository.GetAllWithIncludesAsync();

        // Paths must be matched the way the file system matches them: a case-only difference is
        // the same file on Windows/macOS, and treating it as new would re-discover (and let the
        // user re-import) a book that is already tracked. Preserves the comparer the scan's
        // former GetAllFilePathsAsync load had.
        var knownPaths = audiobooks.Select(a => a.FileInfoFullPath).ToHashSet(AudiobookFileHandler.PathComparer);

        var walk = _treeWalker.Walk(_settings.AudiobookLibraryPath, AudiobookTagHandler.IsSupported);

        var (totalFiles, newFiles, trackedFiles) =
            await _scanService.ScanFilesAsync(walk.SupportedFiles, knownPaths, discoveryProgress);

        // Report the scan's own completion as soon as discovery finishes, before the consistency
        // half starts: if that later half fails, the controller has real discovery counts to rely
        // on rather than zeroing them (rows this phase committed are real and visible either way).
        if (discoveryCompleted is not null)
        {
            await discoveryCompleted(totalFiles, newFiles, trackedFiles);
        }

        var (booksChecked, issuesFound) = await _consistencyService.RunConsistencyCheck(
            consistencyProgress,
            new ConsistencyCheckInput(audiobooks, walk.Directories));

        return new CombinedScanResult(totalFiles, newFiles, trackedFiles, booksChecked, issuesFound);
    }
}
