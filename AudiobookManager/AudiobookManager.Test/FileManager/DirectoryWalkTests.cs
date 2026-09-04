using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;

/// <summary>
/// Real symlinks on a real temp tree. A mocked filesystem would not reproduce the behaviour under
/// test - the loop comes from the OS walk following a reparse point, not from any code here.
/// </summary>
[TestClass]
public class DirectoryWalkTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"walk-{Guid.NewGuid():N}");
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

    [TestMethod]
    public void EnumerateDirectoriesRecursively_ReturnsEveryRealDescendant()
    {
        Dir("author", "series", "book");
        Dir("other");

        var found = DirectoryWalk.EnumerateDirectoriesRecursively(_root).ToList();

        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.Combine(_root, "author"),
                Path.Combine(_root, "author", "series"),
                Path.Combine(_root, "author", "series", "book"),
                Path.Combine(_root, "other"),
            },
            found);
    }

    // The failure mode this exists for: SearchOption.AllDirectories includes reparse points, and
    // the documentation says outright that a link forming a loop makes the search never terminate.
    [TestMethod]
    public void EnumerateDirectoriesRecursively_ALinkBackToAnAncestor_Terminates()
    {
        var nested = Dir("author", "series");
        Directory.CreateSymbolicLink(Path.Combine(nested, "loop"), _root);

        var found = DirectoryWalk.EnumerateDirectoriesRecursively(_root).ToList();

        CollectionAssert.AreEquivalent(
            new[] { Path.Combine(_root, "author"), Path.Combine(_root, "author", "series") },
            found);
    }

    [TestMethod]
    public void EnumerateDirectoriesRecursively_DoesNotDescendIntoALinkedTree()
    {
        var target = Dir("elsewhere");
        Directory.CreateDirectory(Path.Combine(target, "hidden-behind-the-link"));
        var library = Dir("library");
        Directory.CreateSymbolicLink(Path.Combine(library, "shortcut"), target);

        var found = DirectoryWalk.EnumerateDirectoriesRecursively(library).ToList();

        Assert.AreEqual(0, found.Count, "The link itself is not a directory to walk, and its target belongs to the other branch.");
    }

    [TestMethod]
    public void IsLink_DistinguishesALinkFromARealDirectory()
    {
        var real = Dir("real");
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, real);

        Assert.IsFalse(DirectoryWalk.IsLink(real));
        Assert.IsTrue(DirectoryWalk.IsLink(link));
    }

    [TestMethod]
    public void IsLink_APathThatDoesNotExist_IsNotTreatedAsALink()
    {
        Assert.IsFalse(DirectoryWalk.IsLink(Path.Combine(_root, "no-such-directory")));
    }
}
