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

        _service = new FileService(Options.Create(new AudiobookManagerSettings
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
}
