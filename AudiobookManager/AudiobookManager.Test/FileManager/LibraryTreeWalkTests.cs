using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;

/// <summary>
/// Real symlinks on a real temp tree, mirroring DirectoryWalkTests: the behaviour under test -
/// not descending into a link, flagging it so the orphan sweep treats it as "must keep" - comes
/// from how the OS walk answers, not from any code a mock could stand in for.
/// </summary>
[TestClass]
public class LibraryTreeWalkTests
{
    private string _root = null!;
    private readonly LibraryTreeWalker _walker = new LibraryTreeWalker();

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"treewalk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Delete the links first: a recursive delete over a tree containing a link back to an
        // ancestor is the very thing under test here, and it is not what should be exercised in
        // cleanup.
        foreach (var directory in SafeDescendants(_root).Reverse())
        {
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                Directory.Delete(directory);
            }
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IEnumerable<string> SafeDescendants(string root) =>
        DirectoryWalk.EnumerateDirectoriesRecursively(root)
            .Concat(Directory.Exists(root)
                ? Directory.EnumerateDirectories(root).Where(d => new DirectoryInfo(d).LinkTarget is not null)
                : []);

    private string Dir(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private bool FitsFilter(FileInfo fileInfo) =>
        fileInfo.Extension == ".m4b";

    [TestMethod]
    public void Walk_NestedDirs_ReturnsEveryNonRootDirectoryWithItsImmediateChildren()
    {
        Dir("author", "series", "book");
        Dir("other");

        var walk = _walker.Walk(_root, this.FitsFilter);

        var byPath = walk.Directories.ToDictionary(d => d.Path);
        Assert.IsFalse(byPath.ContainsKey(_root), "root itself is never reported");
        Assert.AreEqual(4, walk.Directories.Count);

        var author = byPath[Path.Combine(_root, "author")];
        CollectionAssert.AreEquivalent(
            new[] { Path.Combine(_root, "author", "series") },
            author.Subdirectories.ToList());
        CollectionAssert.AreEquivalent(
            new[] { Path.Combine(_root, "author", "series", "book") },
            byPath[Path.Combine(_root, "author", "series")].Subdirectories.ToList());
        Assert.AreEqual(0, byPath[Path.Combine(_root, "author", "series", "book")].Subdirectories.Count);
    }

    [TestMethod]
    public void Walk_FilterAccuracy_OnlyMatchingFilesAreReportedSupported()
    {
        var bookDir = Dir("author", "book");
        File.WriteAllText(Path.Combine(bookDir, "audio.m4b"), "fake");
        File.WriteAllText(Path.Combine(bookDir, "notes.txt"), "not audio");

        var walk = _walker.Walk(_root, this.FitsFilter);

        CollectionAssert.AreEquivalent(
            new[] { Path.Combine(bookDir, "audio.m4b") },
            walk.SupportedFiles.Select(f => f.FullPath).ToList());
        Assert.IsTrue(walk.Directories.Single(d => d.Path == bookDir).HasSupportedAudioFile);
    }

    [TestMethod]
    public void Walk_SymlinkedChild_IsNotDescendedIntoButFlagged()
    {
        // The link's target lives outside the walked root, so it can never be walked in its own
        // right - like a media directory on another share.
        var targetDir = Path.Combine(Path.GetTempPath(), $"treewalk-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "hidden.m4b"), "fake");

        var library = Dir("library");
        var linkPath = Path.Combine(library, "shortcut");
        Directory.CreateSymbolicLink(linkPath, targetDir);

        try
        {
            var walk = _walker.Walk(_root, this.FitsFilter);

            // Nothing from the link's target can appear: the link itself is not walked.
            Assert.AreEqual(0, walk.SupportedFiles.Count,
                "files behind the link must not be reported - the walker never descends into links");

            // But the link is flagged: the orphan sweep must not reclaim a directory whose child
            // is a link, because nothing ever answered for what is under it.
            var libraryDir = walk.Directories.Single(d => d.Path == library);
            CollectionAssert.AreEquivalent(new[] { linkPath }, libraryDir.Subdirectories.ToList());
            Assert.IsTrue(libraryDir.HasLinkSubdirectory);
            Assert.IsFalse(walk.Directories.Any(d => d.Path == targetDir),
                "the link's target is not a directory of this library");
        }
        finally
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
    }

    [TestMethod]
    public void Walk_ALinkBackToAnAncestor_TerminatesAndIsFlagged()
    {
        var nested = Dir("author", "series");
        Directory.CreateSymbolicLink(Path.Combine(nested, "loop"), _root);

        var walk = _walker.Walk(_root, this.FitsFilter);

        // The walk terminates (a link back to an ancestor would never terminate if followed) and
        // the looping link still shows up as a link child of "author/series".
        Assert.IsTrue(walk.Directories.Single(d => d.Path == nested).HasLinkSubdirectory);
        CollectionAssert.AreEquivalent(
            new[] { Path.Combine(_root, "author"), nested },
            walk.Directories.Select(d => d.Path).ToList());
    }

    [TestMethod]
    public void Walk_HasSupportedAudioFile_IsTheDirectChildrenOnly()
    {
        var bookDir = Dir("author", "book");
        File.WriteAllText(Path.Combine(bookDir, "audio.m4b"), "fake");

        var walk = _walker.Walk(_root, this.FitsFilter);

        var book = walk.Directories.Single(d => d.Path == bookDir);
        var author = walk.Directories.Single(d => d.Path == Path.Combine(_root, "author"));
        Assert.IsTrue(book.HasSupportedAudioFile);
        Assert.IsFalse(author.HasSupportedAudioFile,
            "the audio sits in a child directory - the parent must not claim it directly");
        Assert.IsFalse(walk.Directories.Single(d => d.Path == bookDir).HasLinkSubdirectory);
    }

    // The walker's supported-file output has to stay exactly what the scan consumed - the scan's
    // whole input set is built from it, and with the combined scan the orphan sweep runs off the
    // same walk.
    [TestMethod]
    public void Walk_SupportedFiles_EqualFileScannerOutputForTheSameTree()
    {
        Dir("author", "series", "book");
        Dir("other");
        File.WriteAllText(Path.Combine(_root, "root.m4b"), "fake");
        File.WriteAllText(Path.Combine(_root, "author", "series", "book", "deep.m4b"), "fake");
        File.WriteAllText(Path.Combine(_root, "other", "notes.txt"), "not audio");
        File.WriteAllText(Path.Combine(_root, "other", "book.mp3"), "not m4b");

        var walk = _walker.Walk(_root, this.FitsFilter);
        var scannerOutput = FileScanner.ScanDirectoryForFiles(_root, this.FitsFilter);

        CollectionAssert.AreEqual(
            scannerOutput.Select(f => f.FullPath).ToList(),
            walk.SupportedFiles.Select(f => f.FullPath).ToList());
    }
}
