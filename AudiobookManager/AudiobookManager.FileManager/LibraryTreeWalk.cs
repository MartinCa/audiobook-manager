using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;

/// <summary>
/// One non-root directory beneath the library root, reduced to the facts the library-wide
/// consumers need without walking the tree again themselves:
///
/// - <see cref="Subdirectories"/> / <see cref="HasLinkSubdirectory"/> let the orphan-directory
///   sweep answer "must this subtree be kept?" from the children's already-computed answers
///   instead of re-enumerating each directory when it gets to it.
/// - <see cref="HasSupportedAudioFile"/> is the sweep's "holds audio" test, computed once per
///   directory during the single walk rather than with a second <c>EnumerateFiles</c> pass.
/// </summary>
public sealed record LibraryDirectory(
    string Path,
    /// <summary>Immediate child directory paths, links included.</summary>
    IReadOnlyList<string> Subdirectories,
    /// <summary>Whether any immediate child file matches the walk's supported-file filter.</summary>
    bool HasSupportedAudioFile,
    /// <summary>Whether any immediate child is a link (so its contents were never examined).</summary>
    bool HasLinkSubdirectory);

/// <summary>
/// What one library walk produced: the supported files (the scan's input) and one
/// <see cref="LibraryDirectory"/> per non-root directory (the consistency orphan sweep's input).
/// Built together so the library is traversed exactly once for both, instead of once by the
/// scanner and again by the orphan sweep.
/// </summary>
public sealed record LibraryWalkResult(
    IReadOnlyList<AudiobookFileInfo> SupportedFiles,
    IReadOnlyList<LibraryDirectory> Directories);

public interface ILibraryTreeWalker
{
    /// <summary>
    /// Walks <paramref name="root"/> once, iteratively, without ever descending into a link.
    /// The returned <see cref="LibraryWalkResult.SupportedFiles"/> matches
    /// <see cref="FileScanner.ScanDirectoryForFiles"/> output for the same tree and filter.
    /// </summary>
    LibraryWalkResult Walk(string root, Func<FileInfo, bool> supportedFileFilter);
}

/// <summary>
/// The single traversal that backs both the library scan and the consistency orphan sweep.
///
/// Same iterative stack discipline as <see cref="FileScanner.ScanDirectoryForFiles"/> and the
/// same <see cref="DirectoryWalk.IsLink"/> never-recurse-into-links rule as it and
/// <see cref="DirectoryWalk.EnumerateDirectoriesRecursively"/>: links back to an ancestor would
/// otherwise make the walk never terminate, and the same file reached through two paths would be
/// scanned twice.
/// </summary>
public sealed class LibraryTreeWalker : ILibraryTreeWalker
{
    public LibraryWalkResult Walk(string root, Func<FileInfo, bool> supportedFileFilter)
    {
        var supportedFiles = new List<AudiobookFileInfo>();
        var directories = new List<LibraryDirectory>();
        var pending = new Stack<string>();
        pending.Push(root);

        // Single accumulating list with lazy Enumerate* calls, the same shape as
        // ScanDirectoryForFiles: no recursive GetFiles/GetDirectories arrays materialized (each
        // would allocate its own list per level, merged via AddRange - O(depth) intermediate
        // allocations on deep trees).
        while (pending.Count > 0)
        {
            var currentPath = pending.Pop();

            var subdirectories = new List<string>();
            var hasSupportedAudioFile = false;
            var hasLinkSubdirectory = false;

            foreach (string filePath in Directory.EnumerateFiles(currentPath))
            {
                var fileInfo = new FileInfo(filePath);
                if (supportedFileFilter is null || supportedFileFilter(fileInfo))
                {
                    hasSupportedAudioFile = true;
                    supportedFiles.Add(new AudiobookFileInfo(fileInfo));
                }
            }

            foreach (string subdirectory in Directory.EnumerateDirectories(currentPath))
            {
                subdirectories.Add(subdirectory);

                // Not into symlinks: a link back to an ancestor turns this walk into one that
                // never terminates, and the same file reached through two paths would be scanned
                // twice. See DirectoryWalk.
                if (DirectoryWalk.IsLink(subdirectory))
                {
                    hasLinkSubdirectory = true;
                    continue;
                }

                pending.Push(subdirectory);
            }

            if (currentPath != root)
            {
                directories.Add(new LibraryDirectory(
                    currentPath, subdirectories, hasSupportedAudioFile, hasLinkSubdirectory));
            }
        }

        return new LibraryWalkResult(supportedFiles, directories);
    }
}
