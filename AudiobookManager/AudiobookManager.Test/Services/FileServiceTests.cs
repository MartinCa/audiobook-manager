using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Services;

/// <summary>
/// FileService is the only place that turns a client-supplied path into a directory listing or a
/// recursive delete, so its "is this inside an allowed base?" check is load-bearing.
/// </summary>
[TestClass]
public class FileServiceTests
{
    private string _root = null!;
    private string _importPath = null!;
    private string _libraryPath = null!;
    private FileService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"fileservice-{Guid.NewGuid():N}");
        _importPath = Path.Combine(_root, "import");
        _libraryPath = Path.Combine(_root, "library");
        Directory.CreateDirectory(_importPath);
        Directory.CreateDirectory(_libraryPath);

        var fileOperations = new FileOperations();
        var fileHandler = new AudiobookFileHandler(fileOperations);

        _service = new FileService(
            fileOperations,
            fileHandler,
            Options.Create(new AudiobookManagerSettings
            {
                AudiobookImportPath = _importPath,
                AudiobookLibraryPath = _libraryPath,
            }));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [TestMethod]
    public void DeleteDirectory_SiblingSharingTheLibraryPathAsANamePrefix_IsRejectedAndNotDeleted()
    {
        // Regression: the allowed-base check used a bare string StartsWith, so a sibling whose
        // name merely began with the library path ("<root>/library-backup" vs "<root>/library")
        // passed validation - and this method deletes recursively.
        var sibling = Path.Combine(_root, "library-backup");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "keep-me.txt"), "important");

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => _service.DeleteDirectory(sibling));
        Assert.IsTrue(Directory.Exists(sibling), "the sibling directory must not have been deleted");
        Assert.IsTrue(File.Exists(Path.Combine(sibling, "keep-me.txt")));
    }

    [TestMethod]
    public void GetDirectoryContents_SiblingSharingTheImportPathAsANamePrefix_IsRejected()
    {
        var sibling = Path.Combine(_root, "import-old");
        Directory.CreateDirectory(sibling);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => _service.GetDirectoryContents(sibling));
    }

    [TestMethod]
    public void GetDirectoryContents_PathTraversalEscapingTheAllowedBases_IsRejected()
    {
        var escaped = Path.Combine(_libraryPath, "..", "..");

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => _service.GetDirectoryContents(escaped));
    }

    [TestMethod]
    public void GetDirectoryContents_DirectoryInsideAnAllowedBase_IsAllowed()
    {
        var inside = Path.Combine(_libraryPath, "Author", "2020 - Book");
        Directory.CreateDirectory(inside);

        var contents = _service.GetDirectoryContents(inside);

        Assert.AreEqual(0, contents.Count);
    }

    [TestMethod]
    public void DeleteDirectory_DirectoryInsideAnAllowedBase_IsDeleted()
    {
        var inside = Path.Combine(_importPath, "A Book");
        Directory.CreateDirectory(inside);

        _service.DeleteDirectory(inside);

        Assert.IsFalse(Directory.Exists(inside));
    }

    [TestMethod]
    public void DeleteDirectory_LibraryRoot_ThrowsInvalidOperationExceptionAndDoesNotDelete()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _service.DeleteDirectory(_libraryPath));
        Assert.IsTrue(Directory.Exists(_libraryPath));
    }

    [TestMethod]
    public void DeleteDirectory_ImportRoot_ThrowsInvalidOperationExceptionAndDoesNotDelete()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _service.DeleteDirectory(_importPath));
        Assert.IsTrue(Directory.Exists(_importPath));
    }

    [TestMethod]
    public void DeleteDirectory_FileDirectlyInImportRoot_DeletesOnlyFileNotImportRoot()
    {
        var fileInRoot = Path.Combine(_importPath, "standalone.m4b");
        var otherFile = Path.Combine(_importPath, "other.m4b");
        File.WriteAllText(fileInRoot, "test1");
        File.WriteAllText(otherFile, "test2");

        _service.DeleteDirectory(fileInRoot);

        Assert.IsFalse(File.Exists(fileInRoot));
        Assert.IsTrue(File.Exists(otherFile));
        Assert.IsTrue(Directory.Exists(_importPath));
    }

    [TestMethod]
    public void GetDirectoryContents_FileDirectlyInImportRoot_ReturnsOnlyThatFileNotWholeDirectory()
    {
        var fileInRoot = Path.Combine(_importPath, "standalone.m4b");
        var otherFile = Path.Combine(_importPath, "other.m4b");
        File.WriteAllText(fileInRoot, "test1");
        File.WriteAllText(otherFile, "test2");

        var contents = _service.GetDirectoryContents(fileInRoot);

        Assert.AreEqual(1, contents.Count);
        Assert.AreEqual("standalone.m4b", contents[0].FileName);
    }

    [TestMethod]
    public void GetCoverPath_SiblingCoverJpgExists_ReturnsItsPath()
    {
        var bookDir = Path.Combine(_libraryPath, "Author", "2020 - Book");
        Directory.CreateDirectory(bookDir);
        var m4bPath = Path.Combine(bookDir, "book.m4b");
        var coverPath = Path.Combine(bookDir, "cover.jpg");
        File.WriteAllText(m4bPath, "fake");
        File.WriteAllBytes(coverPath, new byte[] { 0xFF, 0xD8, 0xFF });

        var result = _service.GetCoverPath(m4bPath);

        Assert.AreEqual(coverPath, result);
    }

    [TestMethod]
    public void GetCoverPath_NoCoverSidecar_ReturnsNull()
    {
        var bookDir = Path.Combine(_libraryPath, "Author", "2020 - Book");
        Directory.CreateDirectory(bookDir);
        var m4bPath = Path.Combine(bookDir, "book.m4b");
        File.WriteAllText(m4bPath, "fake");

        var result = _service.GetCoverPath(m4bPath);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetCoverPath_PathOutsideAllowedBases_IsRejected()
    {
        var outside = Path.Combine(_root, "book.m4b");
        File.WriteAllText(outside, "fake");

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => _service.GetCoverPath(outside));
    }

    // Regression guard for the same class of bug GetExistingCoverPath's cleanupDuplicate param
    // exists for: a passive cover lookup must never delete anything, even when both a cover.jpg
    // and a cover.png sit beside the file being previewed.
    [TestMethod]
    public void GetCoverPath_BothJpgAndPngExist_DeletesNeitherFile()
    {
        var bookDir = Path.Combine(_libraryPath, "Author", "2020 - Book");
        Directory.CreateDirectory(bookDir);
        var m4bPath = Path.Combine(bookDir, "book.m4b");
        var jpgPath = Path.Combine(bookDir, "cover.jpg");
        var pngPath = Path.Combine(bookDir, "cover.png");
        File.WriteAllText(m4bPath, "fake");
        File.WriteAllBytes(jpgPath, new byte[] { 0xFF, 0xD8, 0xFF });
        File.WriteAllBytes(pngPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var result = _service.GetCoverPath(m4bPath);

        Assert.AreEqual(jpgPath, result);
        Assert.IsTrue(File.Exists(jpgPath));
        Assert.IsTrue(File.Exists(pngPath));
    }

    [TestMethod]
    public void DeleteDirectory_DirectoryWithPeriodInName_DeletesDirectory()
    {
        var dirWithDot = Path.Combine(_importPath, "J.R.R. Tolkien");
        Directory.CreateDirectory(dirWithDot);
        File.WriteAllText(Path.Combine(dirWithDot, "book.m4b"), "test");

        _service.DeleteDirectory(dirWithDot);

        Assert.IsFalse(Directory.Exists(dirWithDot));
    }

    [TestMethod]
    public void GetDirectoryContents_DirectoryWithPeriodInName_ReturnsAllFiles()
    {
        var dirWithDot = Path.Combine(_importPath, "J.R.R. Tolkien");
        Directory.CreateDirectory(dirWithDot);
        File.WriteAllText(Path.Combine(dirWithDot, "book1.m4b"), "test1");
        File.WriteAllText(Path.Combine(dirWithDot, "book2.m4b"), "test2");

        var contents = _service.GetDirectoryContents(dirWithDot);

        Assert.AreEqual(2, contents.Count);
    }
}
