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

    public async Task<int> ScanAsync(
        Func<string, int, int, int, Task> progressAction,
        int totalBooks,
        int issuesFound,
        IReadOnlyList<LibraryDirectory> directories)
    {
        if (!Directory.Exists(_settings.AudiobookLibraryPath))
        {
            return issuesFound;
        }

        // A single directory walk, sorted deepest-first.
        //
        // The walk itself happened once, up front in the combined scan, and every directory
        // carries the facts this sweep would otherwise have to re-enumerate per directory:
        // whether it directly holds a supported file, its immediate children, and whether any
        // child is a link. One walk, one stat per file, for both the scan and this sweep.
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
        //
        // These directory facts are a snapshot from the walk's point in time, which ran before the
        // per-book consistency loop. A file copied into the library during that window is not in
        // this walk, so its directory can be reported as an orphan until the next run. That is
        // staleness, not data loss: resolving re-enumerates the directory from disk and refuses to
        // delete anything but this app's own sidecars (see DeleteOrphanDirectoryFromDisk), so a
        // just-arrived book is preserved and the next check reports the directory correctly.
        var allDirectories = directories
            .OrderByDescending(directory => directory.Path.Count(
                c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ToList();

        // Directories that must not be reclaimed - because they hold audio, or because something
        // under them could not be looked inside. Both answers travel upwards identically: a parent
        // whose child must be kept must be kept too.
        var mustKeep = new HashSet<string>(AudiobookFileHandler.PathComparer);
        var orphans = new List<OrphanDirectory>();
        var orphansByPath = new Dictionary<string, OrphanDirectory>(AudiobookFileHandler.PathComparer);

        foreach (var directory in allDirectories)
        {
            // Three separate reasons to keep a directory, and only the first is about audio: it
            // holds a supported file, a child of it is already being kept, or a child is a symlink.
            //
            // The last is why this is "must keep" rather than "has audio". A link's target is
            // deliberately not walked, so nothing answers for what is under it - and a missing
            // answer would otherwise read as "no audio under there", which is the one reading that
            // ends in a recursive delete of a folder whose contents were never examined.
            var mustKeepDirectory =
                directory.HasSupportedAudioFile
                || directory.Subdirectories.Any(mustKeep.Contains)
                || directory.HasLinkSubdirectory;

            if (mustKeepDirectory)
            {
                mustKeep.Add(directory.Path);
                continue;
            }

            // Nothing under here is audio, so the whole subtree is reclaimable. Report only this
            // directory: deleting it removes the children anyway, and listing both would make the
            // user resolve the same folder twice.
            foreach (var subdirectory in directory.Subdirectories)
            {
                if (orphansByPath.Remove(subdirectory, out var superseded))
                {
                    orphans.Remove(superseded);
                    issuesFound--;
                }
            }

            var orphan = new OrphanDirectory
            {
                DirectoryPath = directory.Path,
                DetectedAt = DateTime.UtcNow
            };
            orphans.Add(orphan);
            orphansByPath[directory.Path] = orphan;
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
                "Orphan directory '{DirectoryPath}' is not empty; skipped disk deletion and removed from orphan list.",
                directory.DirectoryPath);
            actionTaken = "retained_not_empty";
            message = "Directory still contains files; preserved directory on disk and removed from orphan list.";
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
        //
        // Refuses on anything that is not a file this application itself knows is disposable -
        // not just supported audio formats. A directory holding an mp3/flac/opus audiobook, a
        // PDF, an epub or bonus content is just as much "content the user would not expect this
        // to delete" as an m4b would be - IsSupported only tells the rest of the app "can be
        // organized/tagged", it was never a definition of "safe to discard".
        //
        // But "any file at all" over-corrected: the ordinary way a directory actually becomes
        // orphaned is a book's m4b being reorganized or deleted out of it, which routinely leaves
        // exactly the sidecars this app generates beside that book (cover.jpg, metadata.opf,
        // desc.txt, reader.txt - see AudiobookFileHandler.IsSidecarFileName) - and, just as
        // routinely on a real filesystem, OS-generated junk (.DS_Store, Thumbs.db, desktop.ini).
        // Refusing to delete on those meant almost no real orphan ever actually got cleaned up:
        // the one case this feature exists for was silently defeated by the same fix that closed
        // the data-loss gap. Both lists are explicit and narrow on purpose - anything not on them
        // still blocks deletion exactly as before.
        var directoriesToCheck = new[] { directoryPath }
            .Concat(DirectoryWalk.EnumerateDirectoriesRecursively(directoryPath));

        var hasNonDiscardableFile = directoriesToCheck
            .SelectMany(Directory.EnumerateFiles)
            .Any(file => !IsSafeToDiscard(file));
        if (hasNonDiscardableFile)
        {
            return false;
        }

        _fileOperations.DeleteDirectory(directoryPath, recursive: true, "resolving orphan directory");
        return true;
    }

    // Fixed names written by the OS or the file-sharing client, never by this app or by a user -
    // finding one of these and nothing else is exactly as harmless as finding an empty directory.
    // Matched case-insensitively regardless of platform: a share can carry a Thumbs.db left by a
    // Windows client even when this app runs on Linux, so the platform-dependent PathComparison
    // AudiobookFileHandler uses for its own sidecars (which this app writes itself, in a fixed
    // case) is not the right rule for names other tools chose.
    private static readonly HashSet<string> _harmlessSystemFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
        ".directory", // KDE's Thumbs.db/desktop.ini equivalent.
    };

    private static bool IsSafeToDiscard(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return AudiobookFileHandler.IsSidecarFileName(fileName) || _harmlessSystemFileNames.Contains(fileName);
    }
}
