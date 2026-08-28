using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Ordering of the user-facing name lists. These run against real SQLite because the whole point
/// is which collation does the sorting: an ORDER BY that looks equivalent in LINQ behaves
/// differently once EF translates it into SQL (BINARY, i.e. by code point).
/// </summary>
[TestClass]
public class AudiobookRepositoryOrderingTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"audiobookordering-{Guid.NewGuid():N}.db");
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
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    // persons.name is unique, so every book has to reuse the same tracked Person instance
    // rather than constructing a fresh one per seed.
    private Person? _defaultAuthor;

    private async Task SeedAsync(string bookName, string? series, Person? author = null)
    {
        _defaultAuthor ??= new Person(default, "An Author");

        var audiobook = new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { author ?? _defaultAuthor }
        };

        await _repository.InsertAudiobook(audiobook);
    }

    [TestMethod]
    public async Task GetSeriesNamesAsync_OrdersForAReaderNotByCodePoint()
    {
        // Regression: BINARY collation would put "Zeta Series" first and "Elan Series" after it.
        await SeedAsync("Book A", "alpha series");
        await SeedAsync("Book B", "Zeta Series");
        await SeedAsync("Book C", "Elan Series");
        await SeedAsync("Book D", "beta series");

        var series = await _repository.GetSeriesNamesAsync();

        CollectionAssert.AreEqual(
            new List<string> { "alpha series", "beta series", "Elan Series", "Zeta Series" },
            series);
    }

    [TestMethod]
    public async Task GetSeriesNamesAsync_CollapsesRepeatsAndExcludesBlankSeries()
    {
        await SeedAsync("Book A", "Shared Series");
        await SeedAsync("Book B", "Shared Series");
        await SeedAsync("Book C", null);
        await SeedAsync("Book D", "");

        var series = await _repository.GetSeriesNamesAsync();

        CollectionAssert.AreEqual(new List<string> { "Shared Series" }, series);
    }

    [TestMethod]
    public async Task GetSeriesCountsByAuthorAsync_OrdersForAReaderAndCountsPerSeries()
    {
        var author = new Person(default, "Target Author");
        await SeedAsync("Book A", "alpha series", author);
        await SeedAsync("Book B", "alpha series", author);
        await SeedAsync("Book C", "Zeta Series", author);
        await SeedAsync("Book D", "Elan Series", author);

        var counts = await _repository.GetSeriesCountsByAuthorAsync(author.Id);

        CollectionAssert.AreEqual(
            new List<string> { "alpha series", "Elan Series", "Zeta Series" },
            counts.Select(c => c.Series).ToList());
        Assert.AreEqual(2, counts[0].BookCount);
        Assert.AreEqual(1, counts[1].BookCount);
        Assert.AreEqual(1, counts[2].BookCount);
    }

    [TestMethod]
    public async Task GetStandaloneBooksByAuthorAsync_OrdersByTitleForAReaderAndExcludesSeriesBooks()
    {
        var author = new Person(default, "Target Author");
        await SeedAsync("apple book", null, author);
        await SeedAsync("Zebra book", null, author);
        await SeedAsync("Emile book", null, author);
        await SeedAsync("In A Series", "Some Series", author);

        var books = await _repository.GetStandaloneBooksByAuthorAsync(author.Id);

        CollectionAssert.AreEqual(
            new List<string> { "apple book", "Emile book", "Zebra book" },
            books.Select(b => b.BookName).ToList());
    }
}
