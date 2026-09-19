using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// <see cref="AudiobookRepository.GetStandaloneOwnedKeysByAuthorAsync"/> against real SQLite - the
/// author-roster counterpart of <see cref="AudiobookRepositorySeriesOwnedBooksPageTests"/>: it
/// backs <see cref="AudiobookManager.Services.AuthorReconciliationProvider"/>'s owned-key
/// matching, so the property under test is which books it does and does not include (no series,
/// belongs to the right author) and the cap/overflow contract.
/// </summary>
[TestClass]
public class AudiobookRepositoryStandaloneOwnedKeysTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;
    private Person _author = null!;
    private Person _otherAuthor = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"standaloneowned-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
        _author = new Person(default, "Brandon Sanderson");
        _otherAuthor = new Person(default, "Robert Jordan");
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

    private async Task<Audiobook> SeedBookAsync(string bookName, string? series, Person author)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, 7200,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { author },
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    [TestMethod]
    public async Task GetStandaloneOwnedKeysByAuthorAsync_IncludesOnlyThisAuthorsSeriesLessBooks()
    {
        await SeedBookAsync("Elantris", series: null, _author);
        await SeedBookAsync("Mistborn: The Final Empire", series: "Mistborn", _author);
        await SeedBookAsync("The Eye of the World", series: null, _otherAuthor);

        var (keys, overflow) = await _repository.GetStandaloneOwnedKeysByAuthorAsync(_author.Id, 100);

        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual("Elantris", keys.Single().BookName);
        Assert.IsFalse(overflow);
    }

    [TestMethod]
    public async Task GetStandaloneOwnedKeysByAuthorAsync_EmptyStringSeriesCountsAsStandalone()
    {
        await SeedBookAsync("Warbreaker", series: "", _author);

        var (keys, overflow) = await _repository.GetStandaloneOwnedKeysByAuthorAsync(_author.Id, 100);

        Assert.AreEqual(1, keys.Count);
        Assert.IsFalse(overflow);
    }

    [TestMethod]
    public async Task GetStandaloneOwnedKeysByAuthorAsync_MoreRowsThanTheCap_ReportsOverflow()
    {
        for (var i = 0; i < 3; i++)
        {
            await SeedBookAsync($"Book {i}", series: null, _author);
        }

        var (keys, overflow) = await _repository.GetStandaloneOwnedKeysByAuthorAsync(_author.Id, 2);

        Assert.IsTrue(overflow);
        Assert.IsTrue(keys.Count >= 2);
    }

    [TestMethod]
    public async Task GetStandaloneOwnedKeysByAuthorAsync_UnknownAuthor_ReturnsEmpty()
    {
        var (keys, overflow) = await _repository.GetStandaloneOwnedKeysByAuthorAsync(999, 100);

        Assert.AreEqual(0, keys.Count);
        Assert.IsFalse(overflow);
    }
}
