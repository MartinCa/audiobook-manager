using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Paging against real SQLite, because the property under test is one an in-memory list cannot
/// disprove: whether the SQL order is total. A partial ORDER BY lets the database return ties in
/// any order it likes, and two pages taken from different orderings silently repeat one row and
/// drop another.
/// </summary>
[TestClass]
public class AudiobookRepositoryDirtyUrlPagingTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dirtyurlpaging-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // persons.name is unique-indexed (see the genre/author race-fix migration), so each book needs
    // its own author name.
    private static string UniqueAuthorName() => $"An Author {Guid.NewGuid():N}";

    private async Task<Audiobook> SeedBookAsync(string bookName, string? www = null, string? authorName = null)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, www, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, authorName ?? UniqueAuthorName()) }
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    [TestMethod]
    public async Task GetDirtyUrlPageAsync_ReturnsTheRequestedSliceAndTheFullTotal()
    {
        for (var i = 0; i < 25; i++)
        {
            await SeedBookAsync($"Book {i:02d}", "https://www.audible.com/pd/X?ref=x");
        }

        var (items, totalCount) = await _repository.GetDirtyUrlPageAsync(limit: 10, offset: 10);

        Assert.AreEqual(10, items.Count);
        Assert.AreEqual(25, totalCount, "The total is the whole matching set, not the page.");
    }

    [TestMethod]
    public async Task GetDirtyUrlPageAsync_ProjectsIdBookNameAuthorsAndWww()
    {
        await SeedBookAsync("A Book", "https://www.audible.com/pd/X?ref=x", "A Named Author");

        var (items, _) = await _repository.GetDirtyUrlPageAsync(limit: 10, offset: 0);

        Assert.AreEqual(1, items.Count);
        var row = items[0];
        Assert.AreEqual("A Book", row.BookName);
        Assert.AreEqual("A Named Author", row.Authors.Single());
        Assert.AreEqual("https://www.audible.com/pd/X?ref=x", row.Www);
        Assert.IsTrue(row.AudiobookId > 0);
    }

    [TestMethod]
    public async Task GetDirtyUrlPageAsync_ExcludesBooksWithNoUrlOrNeverDirtyUrl()
    {
        await SeedBookAsync("No URL");
        await SeedBookAsync("Blank URL", "");
        // A URL with neither query string nor fragment is already what BookUrlCleaner would
        // produce - nothing to strip, so it is not "dirty" and must not occupy a page slot.
        await SeedBookAsync("Clean URL", "https://hardcover.app/books/connections");
        await SeedBookAsync("Dirty Query URL", "https://www.audible.com/pd/X?ref=x");
        await SeedBookAsync("Dirty Fragment URL", "https://www.audible.com/pd/Y#pageLoadId");

        var (items, totalCount) = await _repository.GetDirtyUrlPageAsync(limit: 10, offset: 0);

        Assert.AreEqual(2, totalCount);
        // Ordered by BookName then Id (the total order every page relies on not drifting).
        Assert.AreSequenceEqual(new[] { "Dirty Fragment URL", "Dirty Query URL" }, items.Select(i => i.BookName));
    }

    // The bug paging invites: every page taken separately, and each row appearing exactly once
    // across all of them.
    [TestMethod]
    public async Task GetDirtyUrlPageAsync_PagedRightThrough_CoversEveryRowExactlyOnce()
    {
        for (var bookNo = 0; bookNo < 7; bookNo++)
        {
            // Several books per name: BookName is not unique, which is exactly why the order
            // has to be total (BookName then Id) for paging to be stable.
            await SeedBookAsync("Same Name", "https://www.audible.com/pd/X?ref=x");
        }

        var seen = new List<long>();
        for (var page = 0; page < 4; page++)
        {
            var (items, _) = await _repository.GetDirtyUrlPageAsync(limit: 2, offset: page * 2);
            seen.AddRange(items.Select(i => i.AudiobookId));
        }

        Assert.AreEqual(7, seen.Distinct().Count());
    }

    [TestMethod]
    public async Task CountDirtyUrlsAsync_CountsOnlyDirtyUrls()
    {
        await SeedBookAsync("No URL");
        await SeedBookAsync("Blank URL", "");
        await SeedBookAsync("Clean URL", "https://hardcover.app/books/connections");
        await SeedBookAsync("Dirty URL", "https://www.audible.com/pd/X?ref=x");

        var count = await _repository.CountDirtyUrlsAsync();

        Assert.AreEqual(1, count);
    }
}