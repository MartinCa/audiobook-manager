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
    public async Task SearchAuthorSummariesAsync_UnaccentedQueryMatchesAccentedAuthorName()
    {
        // SQLite's default BINARY collation (which LIKE uses) never folds diacritics, so typing
        // "rene" for "René" would otherwise return nothing - unfriendly for a name search.
        await SeedBookWithAuthorAsync("Le Petit Prince", "Antoine de Saint-Exupéry");

        var results = await _repository.SearchAuthorSummariesAsync("exupery", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Antoine de Saint-Exupéry", results[0].Name);
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

    [TestMethod]
    public async Task GetAuthorNamesAsync_OrdersForAReaderNotByCodePoint()
    {
        // Regression: this list was moved from an in-memory OrderBy into a SQL ORDER BY, which
        // silently swapped .NET's culture-aware comparison for SQLite's BINARY collation. That
        // orders by code point, so every capitalized name sorts before every lowercase one
        // ("Zadie" before "alice") and accented names land after "Z" - visible nonsense in the
        // autocomplete this endpoint feeds.
        await SeedBookWithAuthorAsync("Book A", "alice munro");
        await SeedBookWithAuthorAsync("Book B", "Zadie Smith");
        await SeedBookWithAuthorAsync("Book C", "Avila Author");
        await SeedBookWithAuthorAsync("Book D", "brandon Sanderson");

        var names = await _repository.GetAuthorNamesAsync();

        CollectionAssert.AreEqual(
            new List<string> { "alice munro", "Avila Author", "brandon Sanderson", "Zadie Smith" },
            names);
    }

    [TestMethod]
    public async Task GetAuthorNamesAsync_ExcludesAuthorsWithNoBooks()
    {
        // persons.name is unique, so a name can never appear on two Person rows - the Distinct()
        // in the query is belt-and-braces, and cannot be exercised from here.
        await SeedBookWithAuthorAsync("Book A", "Authoring Author");
        await _repository.GetOrCreatePerson("Orphan Author");

        var names = await _repository.GetAuthorNamesAsync();

        CollectionAssert.AreEqual(new List<string> { "Authoring Author" }, names);
    }
}
