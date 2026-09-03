using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class OrphanDirectoryConsistencyService : IOrphanDirectoryConsistencyService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IOrphanDirectoryRepository _orphanDirectoryRepository;
    private readonly IFileOperations _fileOperations;
    private readonly ILogger<OrphanDirectoryConsistencyService> _logger;

    public OrphanDirectoryConsistencyService(
        IOptions<AudiobookManagerSettings> settings,
        IOrphanDirectoryRepository orphanDirectoryRepository,
        IFileOperations fileOperations,
        ILogger<OrphanDirectoryConsistencyService> logger)
    {
        _settings = settings.Value;
        _orphanDirectoryRepository = orphanDirectoryRepository;
        _fileOperations = fileOperations;
        _logger = logger;
    }

    public async Task<int> ScanAsync(Func<string, int, int, int, Task> progressAction, int totalBooks, int issuesFound)
    {
        await _orphanDirectoryRepository.ClearAllAsync();

        if (!Directory.Exists(_settings.AudiobookLibraryPath))
        {
            return issuesFound;
        }

        // A single recursive enumeration, walked deepest-first.
        //
        // Checking only leaf directories (which this used to do) meant a deleted series was
        // cleaned up one level per run: the check flagged "Author/Series/Book", resolving it
        // deleted that folder, and only the *next* full check noticed "Author/Series" had become
        // a leaf - so the user had to run the check once per level. Bottom-up, a directory whose
        // every subdirectory is itself being reclaimed is reported in the same pass.
        //
        // "Does this subtree hold audio?" is answered from the children's already-computed
        // answers rather than by re-walking the subtree, so every file in the library is stat'ed
        // once for the whole sweep. Asking Directory.EnumerateFiles(dir, "*", AllDirectories) per
        // directory would re-walk each file once per ancestor level.
        var allDirectories = Directory
            .EnumerateDirectories(_settings.AudiobookLibraryPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(directory => directory.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ToList();

        var subtreeHasAudio = new HashSet<string>(AudiobookFileHandler.PathComparer);
        var orphans = new List<OrphanDirectory>();
        var orphansByPath = new Dictionary<string, OrphanDirectory>(AudiobookFileHandler.PathComparer);

        foreach (var directory in allDirectories)
        {
            var subdirectories = Directory.EnumerateDirectories(directory).ToList();

            var hasAudioFile =
                Directory.EnumerateFiles(directory).Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)))
                || subdirectories.Any(subtreeHasAudio.Contains);

            if (hasAudioFile)
            {
                subtreeHasAudio.Add(directory);
                continue;
            }

            // Nothing under here is audio, so the whole subtree is reclaimable. Report only this
            // directory: deleting it removes the children anyway, and listing both would make the
            // user resolve the same folder twice.
            foreach (var subdirectory in subdirectories)
            {
                if (orphansByPath.Remove(subdirectory, out var superseded))
                {
                    orphans.Remove(superseded);
                    issuesFound--;
                }
            }

            var orphan = new OrphanDirectory
            {
                DirectoryPath = directory,
                DetectedAt = DateTime.UtcNow
            };
            orphans.Add(orphan);
            orphansByPath[directory] = orphan;
            issuesFound++;
        }

        // One insert for the whole sweep rather than a SaveChanges per orphaned folder.
        await _orphanDirectoryRepository.InsertRangeAsync(orphans);

        await progressAction("Checked library directories for orphaned folders", totalBooks, totalBooks, issuesFound);

        return issuesFound;
    }

    public async Task<OrphanDirectoryResolveResult> ResolveOrphanDirectory(long orphanDirectoryId)
    {
        var directory = await _orphanDirectoryRepository.GetByIdAsync(orphanDirectoryId);
        if (directory == null)
            throw new KeyNotFoundException($"Orphan directory {orphanDirectoryId} not found");

        var deleted = DeleteOrphanDirectoryFromDisk(directory.DirectoryPath);
        string actionTaken;
        string message;

        if (deleted)
        {
            _logger.LogInformation("Deleted orphan directory from disk: '{DirectoryPath}'", directory.DirectoryPath);
            actionTaken = "deleted";
            message = "Orphan directory deleted from disk.";
        }
        else
        {
            _logger.LogWarning(
                "Orphan directory '{DirectoryPath}' now contains audio files; skipped disk deletion and removed from orphan list.",
                directory.DirectoryPath);
            actionTaken = "retained_has_audio";
            message = "Directory now contains audio files; preserved directory on disk and removed from orphan list.";
        }

        await _orphanDirectoryRepository.DeleteAsync(orphanDirectoryId);

        return new OrphanDirectoryResolveResult(orphanDirectoryId, directory.DirectoryPath, actionTaken, message);
    }

    public async Task<(int resolved, int failed, int retained)> ResolveAllOrphanDirectories()
    {
        var directories = await _orphanDirectoryRepository.GetAllAsync();
        var deleted = 0;
        var retained = 0;

        var (_, _, failed) = await BulkOperationRunner.RunAsync(
            directories,
            async directory =>
            {
                var result = await ResolveOrphanDirectory(directory.Id);
                if (result.ActionTaken == "deleted")
                {
                    deleted++;
                }
                else
                {
                    retained++;
                }
            },
            _logger,
            directory => $"Failed to resolve orphan directory {directory.Id}");

        return (deleted, failed, retained);
    }

    private bool DeleteOrphanDirectoryFromDisk(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return true;
        }

        // Safety net in case a file was added to the directory since it was detected as orphaned
        var hasAudioFile = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)));
        if (hasAudioFile)
        {
            return false;
        }

        _fileOperations.DeleteDirectory(directoryPath, recursive: true, "resolving orphan directory");
        return true;
    }
}
