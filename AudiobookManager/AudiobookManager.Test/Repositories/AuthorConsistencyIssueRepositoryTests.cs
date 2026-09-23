using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Against real SQLite, like <see cref="SeriesConsistencyIssueRepositoryTests"/>: the
/// unique-per-author upsert and the ordering both depend on behavior an in-memory fake cannot
/// disprove.
/// </summary>
[TestClass]
public class AuthorConsistencyIssueRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AuthorConsistencyIssueRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"authorconsistency-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AuthorConsistencyIssueRepository(_db);
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
    public async Task UpsertFailureAsync_CalledTwiceForTheSameAuthor_ReplacesRatherThanDuplicates()
    {
        var author = await SeedAuthorAsync("Brandon Sanderson");

        await _repository.UpsertFailureAsync(author.Id, "first error");
        await _repository.UpsertFailureAsync(author.Id, "second error");

        var (items, totalCount) = await _repository.GetPageWithAuthorAsync(0, 10);

        Assert.AreEqual(1, totalCount);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("second error", items[0].ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteByPersonIdAsync_RemovesTheRow()
    {
        var author = await SeedAuthorAsync("Brandon Sanderson");
        await _repository.UpsertFailureAsync(author.Id, "an error");

        await _repository.DeleteByPersonIdAsync(author.Id);

        var (items, totalCount) = await _repository.GetPageWithAuthorAsync(0, 10);
        Assert.AreEqual(0, totalCount);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public async Task DeleteByPersonIdAsync_NoExistingRow_IsANoOp()
    {
        var author = await SeedAuthorAsync("Brandon Sanderson");

        await _repository.DeleteByPersonIdAsync(author.Id);

        var (_, totalCount) = await _repository.GetPageWithAuthorAsync(0, 10);
        Assert.AreEqual(0, totalCount);
    }

    [TestMethod]
    public async Task GetPageWithAuthorAsync_OrdersNewestFirst_AndIncludesTheAuthor()
    {
        var older = await SeedAuthorAsync("Brandon Sanderson");
        var newer = await SeedAuthorAsync("Patrick Rothfuss");

        await _repository.UpsertFailureAsync(older.Id, "older error");
        await Task.Delay(10);
        await _repository.UpsertFailureAsync(newer.Id, "newer error");

        var (items, totalCount) = await _repository.GetPageWithAuthorAsync(0, 10);

        Assert.AreEqual(2, totalCount);
        Assert.AreEqual("Patrick Rothfuss", items[0].Person.Name);
        Assert.AreEqual("Brandon Sanderson", items[1].Person.Name);
    }
}
