using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// A book relates to several series (audiobook_series), and every series-keyed read has to see it
/// under each of them, with the part it has in that series - not just under the mirrored primary.
/// </summary>
[TestClass]
public class AudiobookRepositoryMultiSeriesTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;
    private Person _author = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"multiseries-{Guid.NewGuid():N}.db");
        _db = new DatabaseContext(
            new DbContextOptions<DatabaseContext>(),
            Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath }));
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
        _author = new Person(default, "Author");
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Seeds a book; the first relation is the primary.</summary>
    private async Task<Audiobook> SeedAsync(string name, params (string Series, string? Part)[] relations)
    {
        var primary = relations.FirstOrDefault();
        var book = new Audiobook(
            default, name, null, primary.Series, primary.Part, 2024,
            null, null, null, null, null, null, null, null, 60,
            $"/library/{name}.m4b", $"{name}.m4b", 10)
        {
            Authors = new List<Person> { _author },
            Narrators = new List<Person>(),
            SeriesRelations = relations
                .Select((r, i) => new AudiobookSeries { SeriesName = r.Series, SeriesPart = r.Part, IsPrimary = i == 0, SortOrder = i })
                .ToList(),
        };
        _db.Audiobooks.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_ListsABookUnderItsSecondarySeriesWithThatSeriesPart()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));
        await SeedAsync("Plain", ("Spinoff", "2"));

        var (items, total) = await _repository.GetSeriesOwnedBooksPageAsync("Spinoff", 0, 10);

        Assert.AreEqual(2, total);
        CollectionAssert.AreEqual(
            new[] { ("Plain", "2", "Spinoff"), ("Crossover", "7", "Main") },
            items.Select(i => (i.BookName, i.SeriesPart, i.PrimarySeries)).ToArray(),
            "ordered by the part in THIS series, and the primary series is reported for the hint");
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_ThePrimarySeriesStillListsTheBookWithItsOwnPart()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Main", 0, 10);

        Assert.AreEqual("1", items.Single().SeriesPart);
    }

    [TestMethod]
    public async Task GetBooksBySeriesAsync_OrdersByThePartInThatSeries()
    {
        await SeedAsync("A", ("Main", "1"), ("Spinoff", "9"));
        await SeedAsync("B", ("Other", "1"), ("Spinoff", "2"));

        var books = await _repository.GetBooksBySeriesAsync("Spinoff", null);

        CollectionAssert.AreEqual(new[] { "B", "A" }, books.Select(b => b.BookName).ToArray());
    }

    [TestMethod]
    public async Task GetSeriesOwnedKeysAsync_ReportsThePartOfTheQueriedSeries()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));

        var (keys, overflow) = await _repository.GetSeriesOwnedKeysAsync("Spinoff", 100);

        Assert.IsFalse(overflow);
        Assert.AreEqual("7", keys.Single().SeriesPart);
        Assert.AreEqual("Spinoff", keys.Single().Series);
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorAsync_YieldsOneKeyPerBookAndSeriesAndOneForAStandaloneBook()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));
        await SeedAsync("Alone");

        var (keys, _) = await _repository.GetOwnedKeysByAuthorAsync(_author.Id, 100);

        CollectionAssert.AreEquivalent(
            new (string, string?, string?)[] { ("Crossover", "Main", "1"), ("Crossover", "Spinoff", "7"), ("Alone", null, null) },
            keys.Select(k => (k.BookName, k.Series, k.SeriesPart)).ToArray());
    }

    [TestMethod]
    public async Task GetStandaloneBooksByAuthorAsync_ExcludesABookThatOnlyHasASecondarySeries()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));
        await SeedAsync("Alone");

        var (items, total) = await _repository.GetStandaloneBooksByAuthorAsync(_author.Id, 10, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Alone", items.Single().BookName);
    }

    [TestMethod]
    public async Task GetSeriesBookCountsAsync_CountsABookInEverySeriesItBelongsTo()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));
        await SeedAsync("Plain", ("Spinoff", "2"));

        var counts = await _repository.GetSeriesBookCountsAsync(new[] { "Main", "Spinoff" });

        Assert.AreEqual(1, counts["Main"]);
        Assert.AreEqual(2, counts["Spinoff"]);
    }

    [TestMethod]
    public async Task GetSeriesNamesAsync_IncludesSecondarySeriesNames()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));

        CollectionAssert.AreEqual(new[] { "Main", "Spinoff" }, await _repository.GetSeriesNamesAsync());
    }

    [TestMethod]
    public async Task GetSeriesPartConflictCandidatesAsync_ComparesThePartInThatSeriesNotThePrimaryPart()
    {
        var other = await SeedAsync("Other", ("Main", "3"), ("Spinoff", "7"));
        var self = await SeedAsync("Self", ("Spinoff", "1"));

        var (spinoff, _) = await _repository.GetSeriesPartConflictCandidatesAsync("Spinoff", self.Id, "7", "", 10);
        var (main, _) = await _repository.GetSeriesPartConflictCandidatesAsync("Main", self.Id, "7", "", 10);

        Assert.AreEqual(other.Id, spinoff.Single().AudiobookId);
        Assert.AreEqual(0, main.Count, "'Other' has part 7 in Spinoff, not in Main");
    }

    [TestMethod]
    public async Task GetSeriesValuesPageAsync_ListsSecondarySeriesToo()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, null, 0, 10);

        Assert.AreEqual(2, total);
        CollectionAssert.AreEqual(new[] { "Main", "Spinoff" }, items);
    }

    [TestMethod]
    public async Task GetBooksBySeriesValuesAsync_FindsABookThroughItsSecondarySeries()
    {
        await SeedAsync("Crossover", ("Main", "1"), ("Spinoff", "7"));

        var books = await _repository.GetBooksBySeriesValuesAsync(new[] { "Spinoff" });

        Assert.AreEqual("Crossover", books.Single().BookName);
        Assert.AreEqual(2, books.Single().SeriesRelations!.Count, "relations are loaded so a rewrite keeps them");
    }
}
