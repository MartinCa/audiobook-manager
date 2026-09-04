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

    public Task ClearAllAsync() => _orphanDirectoryRepository.ClearAllAsync();

    public async Task<int> ScanAsync(Func<string, int, int, int, Task> progressAction, int totalBooks, int issuesFound)
    {
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
        // "Must this subtree be kept?" is answered from the children's already-computed answers
        // rather than by re-walking the subtree, so every file in the library is stat'ed once for
        // the whole sweep. Asking Directory.EnumerateFiles(dir, "*", AllDirectories) per directory
        // would re-walk each file once per ancestor level.
        // DirectoryWalk rather than SearchOption.AllDirectories: that option deliberately includes
        // reparse points, and a symlink pointing back at an ancestor makes the enumeration itself
        // never terminate. Symlinked media directories are ordinary on a NAS library assembled
        // from several shares.
        var allDirectories = DirectoryWalk
            .EnumerateDirectoriesRecursively(_settings.AudiobookLibraryPath)
            .OrderByDescending(directory => directory.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ToList();

        // Directories that must not be reclaimed - because they hold audio, or because something
        // under them could not be looked inside. Both answers travel upwards identically: a parent
        // whose child must be kept must be kept too.
        var mustKeep = new HashSet<string>(AudiobookFileHandler.PathComparer);
        var orphans = new List<OrphanDirectory>();
        var orphansByPath = new Dictionary<string, OrphanDirectory>(AudiobookFileHandler.PathComparer);

        foreach (var directory in allDirectories)
        {
            var subdirectories = Directory.EnumerateDirectories(directory).ToList();

            // Three separate reasons to keep a directory, and only the first is about audio: it
            // holds a supported file, a child of it is already being kept, or a child is a symlink.
            //
            // The last is why this is "must keep" rather than "has audio". A link's target is
            // deliberately not walked, so nothing answers for what is under it - and a missing
            // answer would otherwise read as "no audio under there", which is the one reading that
            // ends in a recursive delete of a folder whose contents were never examined.
            var mustKeepDirectory =
                Directory.EnumerateFiles(directory).Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)))
                || subdirectories.Any(mustKeep.Contains)
                || subdirectories.Any(DirectoryWalk.IsLink);

            if (mustKeepDirectory)
            {
                mustKeep.Add(directory);
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

        // This directory being a link is the one case the walk below cannot answer: enumerating
        // through it would describe the target's contents, and "the target holds no audio" is not
        // a reason to delete a link. Refuse instead.
        //
        // Links *inside* the subtree need no equivalent check. The delete at the end is
        // Directory.Delete(recursive: true), which is documented not to recurse through a reparse
        // point - a symlinked subdirectory is unlinked, and its target is left alone - so a link
        // appearing between this check and the delete cannot take its target with it. Verified on
        // this stack as well as read: a recursive delete of a directory containing a symlink to a
        // populated directory left the target and its files intact.
        if (DirectoryWalk.IsLink(directoryPath))
        {
            return false;
        }

        // Safety net in case a file was added since the directory was detected as orphaned, walked
        // link-free for the same reason the detection is: an enumeration that followed a link
        // would answer for the wrong subtree.
        var directoriesToCheck = new[] { directoryPath }
            .Concat(DirectoryWalk.EnumerateDirectoriesRecursively(directoryPath));

        var hasAudioFile = directoriesToCheck
            .SelectMany(Directory.EnumerateFiles)
            .Any(file => AudiobookTagHandler.IsSupported(new FileInfo(file)));
        if (hasAudioFile)
        {
            return false;
        }

        _fileOperations.DeleteDirectory(directoryPath, recursive: true, "resolving orphan directory");
        return true;
    }
}
