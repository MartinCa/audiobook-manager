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
    public async Task SearchAuthorsAsync_ReturnsMatchingAuthorsWithBooks()
    {
        await SeedBookWithAuthorAsync("Mistborn", "Brandon Sanderson");
        await SeedBookWithAuthorAsync("Dune", "Frank Herbert");

        var results = await _repository.SearchAuthorsAsync("sander", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Brandon Sanderson", results[0].Name);
        Assert.AreEqual(1, results[0].BooksAuthored.Count);
    }

    [TestMethod]
    public async Task SearchAuthorsAsync_ExcludesAuthorsWithNoBooks()
    {
        await _repository.GetOrCreatePerson("Orphan Author");

        var results = await _repository.SearchAuthorsAsync("orphan", 10);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task SearchAuthorsAsync_ReturnsEmptyWhenNoAuthorMatches()
    {
        await SeedBookWithAuthorAsync("Dune", "Frank Herbert");

        var results = await _repository.SearchAuthorsAsync("nonexistent", 10);

        Assert.AreEqual(0, results.Count);
    }
}
