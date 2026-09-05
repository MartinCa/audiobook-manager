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

    // Regression: Genres is a many-to-many with no position column, so nothing about the join
    // table preserves the order genres were assigned in - EF only leaves unchanged links alone
    // and appends new ones, so re-saving the very same set of genres in a different order used to
    // read back in a different, effectively arbitrary order each time (the bug this test guards:
    // applying identical metadata twice produced two different genre strings). Genres have no
    // order semantics anywhere else in the app - TagConsistencyChecker.FormatGenres already
    // sorts them before comparing - so ordering the Include alphabetically at the read side makes
    // the result deterministic without needing a position column.
    [TestMethod]
    public async Task GetByIdWithIncludesAsync_OrdersGenresAlphabeticallyRegardlessOfLinkOrder()
    {
        var zeta = new Genre(default, "Zeta Genre");
        var alpha = new Genre(default, "Alpha Genre");
        var mid = new Genre(default, "Mid Genre");

        var audiobook = new Audiobook(
            default, "Book A", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/Book A.m4b", "Book A.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, "An Author") },
            Genres = new List<Genre> { zeta, alpha, mid }
        };

        var inserted = await _repository.InsertAudiobook(audiobook);
        var bookId = inserted.Id;

        // A fresh context/repository per read, exactly like the request-scoped DbContext the app
        // actually uses - reusing _db's tracked Audiobook would let EF's identity map hand back
        // the navigation as it was first populated in memory instead of re-querying, which would
        // make this test pass for a reason that has nothing to do with the Include ordering.
        CollectionAssert.AreEqual(
            new List<string> { "Alpha Genre", "Mid Genre", "Zeta Genre" },
            (await FreshRead(bookId))!.Genres.Select(g => g.Name).ToList());

        // Re-save the identical genre set, only reordered - as re-applying the same metadata
        // does. Nothing should change: the link rows for Alpha/Mid/Zeta already exist and are
        // untouched, and the read-side ordering must still make the result alphabetical rather
        // than reflecting whatever order this list happened to be assigned in.
        using (var updateDb = NewContext())
        {
            var updateRepo = new AudiobookRepository(updateDb);
            var toUpdate = await updateRepo.GetByIdWithIncludesAsync(bookId);
            var updateGenres = await updateDb.Genres.ToListAsync();
            toUpdate!.Genres = new List<Genre>
            {
                updateGenres.Single(g => g.Name == "Mid Genre"),
                updateGenres.Single(g => g.Name == "Zeta Genre"),
                updateGenres.Single(g => g.Name == "Alpha Genre"),
            };
            await updateRepo.UpdateAudiobookAsync(toUpdate);
        }

        CollectionAssert.AreEqual(
            new List<string> { "Alpha Genre", "Mid Genre", "Zeta Genre" },
            (await FreshRead(bookId))!.Genres.Select(g => g.Name).ToList());
    }

    private DatabaseContext NewContext()
    {
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        return new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
    }

    private async Task<Audiobook?> FreshRead(long id)
    {
        using var db = NewContext();
        return await new AudiobookRepository(db).GetByIdWithIncludesAsync(id);
    }
}
