using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class PendingAuthorRefreshRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PendingAuthorRefreshRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pendingauthor-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PendingAuthorRefreshRepository(_db);
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

    private async Task<Person> SeedAuthorAsync(string name)
    {
        var person = new Person(default, name);
        _db.Persons.Add(person);
        await _db.SaveChangesAsync();
        return person;
    }

    [TestMethod]
    public async Task UpsertAsync_CalledTwiceForTheSameAuthor_ReplacesRatherThanDuplicates()
    {
        var author = await SeedAuthorAsync("Brandon Sanderson");

        await _repository.UpsertAsync(author.Id, "First Spelling", "Hardcover", null);
        await _repository.UpsertAsync(author.Id, "Second Spelling", "Hardcover", "https://hardcover.app/authors/x");

        var row = await _db.PendingAuthorRefreshes.AsNoTracking().SingleAsync();
        Assert.AreEqual("Second Spelling", row.ProposedName);
        Assert.AreEqual("https://hardcover.app/authors/x", row.SourceUrl);
    }

    [TestMethod]
    public async Task DeleteByPersonIdAsync_RemovesOnlyThatAuthorsRow_AndIsANoOpWhenThereIsNone()
    {
        var a = await SeedAuthorAsync("A");
        var b = await SeedAuthorAsync("B");
        await _repository.UpsertAsync(a.Id, "A2", "Hardcover", null);
        await _repository.UpsertAsync(b.Id, "B2", "Hardcover", null);

        await _repository.DeleteByPersonIdAsync(a.Id);
        await _repository.DeleteByPersonIdAsync(a.Id);

        Assert.IsNull(await _repository.GetByPersonIdAsync(a.Id));
        Assert.AreEqual("B2", (await _repository.GetByPersonIdAsync(b.Id))!.ProposedName);
        Assert.AreEqual(1, await _repository.CountAsync());
    }

    [TestMethod]
    public async Task UpsertAfterADelete_InTheSameContext_StoresTheNewProposal()
    {
        // SQLite reuses rowids, so a stale tracked entity from before the set-based delete would
        // otherwise be resolved back for the new row.
        var author = await SeedAuthorAsync("A");
        await _repository.UpsertAsync(author.Id, "Old", "Hardcover", null);
        await _repository.DeleteByPersonIdAsync(author.Id);

        await _repository.UpsertAsync(author.Id, "New", "Hardcover", null);

        Assert.AreEqual("New", (await _repository.GetByPersonIdAsync(author.Id))!.ProposedName);
    }

    [TestMethod]
    public async Task GetPageWithAuthorAsync_PagesNewestFirstWithAStableOrderAndTheAuthorLoaded()
    {
        var authors = new List<Person>();
        for (var i = 0; i < 5; i++)
        {
            authors.Add(await SeedAuthorAsync($"Author {i}"));
        }

        // Identical fetch times: only the id tiebreaker keeps the pages from overlapping.
        var fetchedAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        foreach (var author in authors)
        {
            _db.PendingAuthorRefreshes.Add(new PendingAuthorRefresh
            {
                PersonId = author.Id, ProposedName = $"P{author.Id}", SourceName = "Hardcover", FetchedAt = fetchedAt,
            });
        }

        await _db.SaveChangesAsync();

        var (page1, total1) = await _repository.GetPageWithAuthorAsync(0, 2);
        var (page2, _) = await _repository.GetPageWithAuthorAsync(2, 2);
        var (page3, _) = await _repository.GetPageWithAuthorAsync(4, 2);

        Assert.AreEqual(5, total1);
        var ids = page1.Concat(page2).Concat(page3).Select(p => p.Id).ToList();
        Assert.AreEqual(5, ids.Distinct().Count());
        Assert.AreEqual("Author 0", page1.First().Person.Name);
    }

    [TestMethod]
    public async Task DeletingTheAuthor_CascadesToItsPendingRename()
    {
        var author = await SeedAuthorAsync("A");
        await _repository.UpsertAsync(author.Id, "A2", "Hardcover", null);

        _db.Persons.Remove(author);
        await _db.SaveChangesAsync();

        Assert.AreEqual(0, await _repository.CountAsync());
    }
}
