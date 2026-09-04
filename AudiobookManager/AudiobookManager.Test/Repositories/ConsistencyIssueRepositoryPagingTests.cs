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
public class ConsistencyIssueRepositoryPagingTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private ConsistencyIssueRepository _repository = null!;
    private Person _author = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"consistencypaging-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new ConsistencyIssueRepository(_db);
        _author = new Person(default, "An Author");
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

    private async Task<Audiobook> SeedBookAsync(string bookName)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { _author }
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    private async Task SeedIssuesAsync(long audiobookId, ConsistencyIssueType type, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _db.ConsistencyIssues.Add(new ConsistencyIssue
            {
                AudiobookId = audiobookId,
                IssueType = type,
                Description = $"{type} #{i}",
                DetectedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task GetPageWithAudiobookAsync_ReturnsTheRequestedSliceAndTheFullTotal()
    {
        var book = await SeedBookAsync("A Book");
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.TagMismatch, 25);

        var (items, totalCount) = await _repository.GetPageWithAudiobookAsync(null, skip: 10, take: 10);

        Assert.AreEqual(10, items.Count);
        Assert.AreEqual(25, totalCount, "The total is the whole matching set, not the page.");
    }

    [TestMethod]
    public async Task GetPageWithAudiobookAsync_IncludesTheAudiobookAndItsAuthors()
    {
        var book = await SeedBookAsync("A Book");
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.MissingDescTxt, 1);

        var (items, _) = await _repository.GetPageWithAudiobookAsync(null, skip: 0, take: 10);

        Assert.AreEqual("A Book", items[0].Audiobook.BookName);
        Assert.AreEqual("An Author", items[0].Audiobook.Authors.Single().Name);
    }

    // The bug paging invites: every page taken separately, and each row appearing exactly once
    // across all of them.
    [TestMethod]
    public async Task GetPageWithAudiobookAsync_PagedRightThrough_CoversEveryRowExactlyOnce()
    {
        var first = await SeedBookAsync("First");
        var second = await SeedBookAsync("Second");
        await SeedIssuesAsync(first.Id, ConsistencyIssueType.TagMismatch, 7);
        await SeedIssuesAsync(second.Id, ConsistencyIssueType.TagMismatch, 7);
        await SeedIssuesAsync(first.Id, ConsistencyIssueType.MissingCoverFile, 7);

        var seen = new List<long>();
        for (var page = 0; page < 5; page++)
        {
            var (items, _) = await _repository.GetPageWithAudiobookAsync(null, skip: page * 5, take: 5);
            seen.AddRange(items.Select(i => i.Id));
        }

        Assert.AreEqual(21, seen.Count);
        CollectionAssert.AreEquivalent(
            await _db.ConsistencyIssues.Select(i => i.Id).ToListAsync(),
            seen,
            "Every issue must appear exactly once across the pages.");
    }

    [TestMethod]
    public async Task GetPageWithAudiobookAsync_FilteredByType_CountsAndReturnsOnlyThatType()
    {
        var book = await SeedBookAsync("A Book");
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.TagMismatch, 6);
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.MissingCoverFile, 4);

        var (items, totalCount) = await _repository.GetPageWithAudiobookAsync(
            ConsistencyIssueType.TagMismatch, skip: 0, take: 50);

        Assert.AreEqual(6, totalCount, "The total must count the filter, not the table.");
        Assert.IsTrue(items.All(i => i.IssueType == ConsistencyIssueType.TagMismatch));
    }

    [TestMethod]
    public async Task GetPageWithAudiobookAsync_PastTheEnd_IsEmptyButStillReportsTheTotal()
    {
        var book = await SeedBookAsync("A Book");
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.TagMismatch, 3);

        var (items, totalCount) = await _repository.GetPageWithAudiobookAsync(null, skip: 500, take: 50);

        Assert.AreEqual(0, items.Count);
        Assert.AreEqual(3, totalCount);
    }

    [TestMethod]
    public async Task GetCountsByTypeAsync_CountsEachTypeSeparately()
    {
        var book = await SeedBookAsync("A Book");
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.TagMismatch, 6);
        await SeedIssuesAsync(book.Id, ConsistencyIssueType.MissingCoverFile, 4);

        var counts = await _repository.GetCountsByTypeAsync();

        Assert.AreEqual(6, counts[ConsistencyIssueType.TagMismatch]);
        Assert.AreEqual(4, counts[ConsistencyIssueType.MissingCoverFile]);
        Assert.AreEqual(2, counts.Count, "Types with no issues must not appear.");
    }
}
