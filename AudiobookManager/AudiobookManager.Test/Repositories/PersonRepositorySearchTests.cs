using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class PersonRepositorySearchTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PersonRepository _repository = null!;
    private AudiobookRepository _audiobookRepository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"personrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PersonRepository(_db);
        _audiobookRepository = new AudiobookRepository(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task SeedBookWithAuthorAsync(string bookName, string authorName)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, authorName) }
        };

        await _audiobookRepository.InsertAudiobook(audiobook);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_ReturnsMatchingAuthorsWithBookCount()
    {
        await SeedBookWithAuthorAsync("Mistborn", "Brandon Sanderson");
        await SeedBookWithAuthorAsync("Dune", "Frank Herbert");

        var results = await _repository.SearchAuthorSummariesAsync("sander", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Brandon Sanderson", results[0].Name);
        Assert.AreEqual(1, results[0].BookCount);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_ExcludesAuthorsWithNoBooks()
    {
        await _repository.GetOrCreatePerson("Orphan Author");

        var results = await _repository.SearchAuthorSummariesAsync("orphan", 10);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_ReturnsEmptyWhenNoAuthorMatches()
    {
        await SeedBookWithAuthorAsync("Dune", "Frank Herbert");

        var results = await _repository.SearchAuthorSummariesAsync("nonexistent", 10);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_AllNamesNew_CreatesEveryOneInASingleBatch()
    {
        var result = await _repository.GetOrCreatePersons(new[] { "Brandon Sanderson", "Frank Herbert" });

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result["Brandon Sanderson"].Id != default);
        Assert.IsTrue(result["Frank Herbert"].Id != default);
        Assert.AreNotEqual(result["Brandon Sanderson"].Id, result["Frank Herbert"].Id);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_AllNamesExisting_ReusesExistingRowsWithoutDuplicating()
    {
        var existing = await _repository.GetOrCreatePerson("Brandon Sanderson");

        var result = await _repository.GetOrCreatePersons(new[] { "Brandon Sanderson" });

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(existing.Id, result["Brandon Sanderson"].Id);

        var all = await _db.Persons.Where(p => p.Name == "Brandon Sanderson").ToListAsync();
        Assert.AreEqual(1, all.Count);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_MixOfExistingAndNewNames_ReusesExistingAndCreatesOnlyMissing()
    {
        var existing = await _repository.GetOrCreatePerson("Brandon Sanderson");

        var result = await _repository.GetOrCreatePersons(new[] { "Brandon Sanderson", "Frank Herbert" });

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(existing.Id, result["Brandon Sanderson"].Id);
        Assert.IsTrue(result["Frank Herbert"].Id != default);

        var allHerbert = await _db.Persons.Where(p => p.Name == "Frank Herbert").ToListAsync();
        Assert.AreEqual(1, allHerbert.Count);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_EmptyInput_ReturnsEmptyDictionaryWithoutQuerying()
    {
        var result = await _repository.GetOrCreatePersons(Array.Empty<string>());

        Assert.AreEqual(0, result.Count);
    }
}
