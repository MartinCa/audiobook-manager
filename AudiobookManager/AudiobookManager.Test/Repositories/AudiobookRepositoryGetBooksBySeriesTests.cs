using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// <c>GetBooksBySeriesAsync</c> (the book list under the series detail, browsed by series and
/// optionally author) orders by series part. It shares the property the owned-books page has: the
/// ordering has to be numeric, not under SQLite's BINARY collation - "2" before "17.5", a
/// non-numeric part like "Book 2" after every numeric one, and a blank part (null, empty,
/// whitespace-only) last. Unlike the owned-books page it is not itself paged, but it renders in
/// the same section, so it must present the same order.
/// </summary>
[TestClass]
public class AudiobookRepositoryGetBooksBySeriesTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;
    private Person _author = null!;
    private Person _narrator = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesbooks-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
        // persons.name is unique, so one shared instance per test (the mapping joins books to
        // the same rows) - a fresh Person per book would trip the unique constraint.
        _author = new Person(default, "Brandon Sanderson");
        _narrator = new Person(default, "Michael Kramer");
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

    private async Task<Audiobook> SeedBookAsync(string bookName, string series, string? seriesPart = null)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, seriesPart, 2024,
            null, null, null, null, null, null, null, null, 7200,
            $"/library/{series}/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { _author },
            Narrators = new List<Person> { _narrator },
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    [TestMethod]
    public async Task GetBooksBySeriesAsync_OrdersNumericSeriesPartsByValue()
    {
        var latePart = await SeedBookAsync("Book Five", "Mistborn", seriesPart: "17.5");
        var firstPart = await SeedBookAsync("Book One", "Mistborn", seriesPart: "1");
        var secondPart = await SeedBookAsync("Book Two", "Mistborn", seriesPart: "2");
        var thirdPart = await SeedBookAsync("Book Three", "Mistborn", seriesPart: "3");
        var fourthPart = await SeedBookAsync("Book Four", "Mistborn", seriesPart: "24");

        var books = await _repository.GetBooksBySeriesAsync("Mistborn", authorId: null);

        Assert.AreSequenceEqual(
            new List<long> { firstPart.Id, secondPart.Id, thirdPart.Id, latePart.Id, fourthPart.Id },
            books.Select(b => b.Id).ToList(),
            "numeric series parts order by value (1, 2, 3, 17.5, 24), not lexically");
    }

    [TestMethod]
    public async Task GetBooksBySeriesAsync_OrdersNonNumericSeriesPartsAfterEveryNumeric()
    {
        // "2.5 Bonus" is not parseable as a number, so it belongs after every numeric part -
        // but its text sorts before "3" under BINARY collation, which is what makes this
        // discriminate the numeric ordering from the lexical one.
        var partOne = await SeedBookAsync("Book One", "Mistborn", seriesPart: "1");
        var partTwo = await SeedBookAsync("Book Two", "Mistborn", seriesPart: "2");
        var partThree = await SeedBookAsync("Book Three", "Mistborn", seriesPart: "3");
        var textPart = await SeedBookAsync("Book Interlude", "Mistborn", seriesPart: "2.5 Bonus");

        var books = await _repository.GetBooksBySeriesAsync("Mistborn", authorId: null);

        Assert.AreSequenceEqual(
            new List<long> { partOne.Id, partTwo.Id, partThree.Id, textPart.Id },
            books.Select(b => b.Id).ToList(),
            "a non-numeric part trails every numeric part, no matter what its text would be under BINARY collation");
    }

    [TestMethod]
    public async Task GetBooksBySeriesAsync_OrdersBlankAndNullSeriesPartsLast()
    {
        // Regression: under the old lexical (BINARY collation) order a null or empty series part
        // sorted before every named part; the sort key moves them to the final tier, after
        // numeric and non-numeric parts alike. Tied blank/null parts order by id - this seed
        // order means the null part gets the lower id.
        var noPart = await SeedBookAsync("No Part", "Mistborn", seriesPart: null);
        var blankPart = await SeedBookAsync("Blank Part", "Mistborn", seriesPart: "  ");
        var partOne = await SeedBookAsync("Book One", "Mistborn", seriesPart: "1");
        var partTwo = await SeedBookAsync("Book Two", "Mistborn", seriesPart: "2");
        var textPart = await SeedBookAsync("Book Interlude", "Mistborn", seriesPart: "Part III");

        var books = await _repository.GetBooksBySeriesAsync("Mistborn", authorId: null);

        Assert.AreSequenceEqual(
            new List<long> { partOne.Id, partTwo.Id, textPart.Id, noPart.Id, blankPart.Id },
            books.Select(b => b.Id).ToList(),
            "blank and null series parts come last, after every numeric and non-numeric part");
    }
}