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

    private async Task SeedAsync(
        string bookName, string? series, Person? author = null, string? seriesPart = null,
        int? durationInSeconds = null, string? www = null)
    {
        _defaultAuthor ??= new Person(default, "An Author");

        var audiobook = new Audiobook(
            default, bookName, null, series, seriesPart, 2024,
            null, null, null, null, null, null, www, null, durationInSeconds,
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
    public async Task GetStandaloneBooksByAuthorAsync_OrdersByTitleInSqlAndExcludesSeriesBooks()
    {
        var author = new Person(default, "Target Author");
        await SeedAsync("apple book", null, author);
        await SeedAsync("Zebra book", null, author);
        await SeedAsync("Emile book", null, author);
        await SeedAsync("In A Series", "Some Series", author);

        var (books, total) = await _repository.GetStandaloneBooksByAuthorAsync(author.Id, limit: 10, offset: 0);

        Assert.AreEqual(3, total);
        // BINARY collation, not culture-aware - see the test above.
        Assert.AreSequenceEqual(
            new List<string> { "Emile book", "Zebra book", "apple book" },
            books.Select(b => b.BookName).ToList());
    }

    // Bug 8 (unified owned-book list): the author detail's standalone section gets the same text
    // search and BookSummaryFilter the whole-library book list offers, scoped to the author.
    [TestMethod]
    public async Task GetStandaloneBooksByAuthorAsync_Search_NarrowsToMatchingBooksOnly()
    {
        var author = new Person(default, "Search Author");
        await SeedAsync("The Final Empire", null, author);
        await SeedAsync("The Well of Ascension", null, author);

        var (books, total) = await _repository.GetStandaloneBooksByAuthorAsync(
            author.Id, limit: 10, offset: 0, search: "final empire");

        Assert.AreEqual(1, total);
        Assert.AreEqual("The Final Empire", books.Single().BookName);
    }

    [TestMethod]
    public async Task GetStandaloneBooksByAuthorAsync_Filter_NarrowsByDurationRange()
    {
        var author = new Person(default, "Filter Author");
        await SeedAsync("Short Book", null, author, durationInSeconds: 1800);
        await SeedAsync("Long Book", null, author, durationInSeconds: 36000);

        var (books, total) = await _repository.GetStandaloneBooksByAuthorAsync(
            author.Id, limit: 10, offset: 0,
            filter: new BookSummaryFilter(MaxDurationInSeconds: 3600));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Short Book", books.Single().BookName);
    }

    [TestMethod]
    public async Task GetStandaloneBooksByAuthorAsync_Filter_NarrowsBySource()
    {
        var author = new Person(default, "Source Author");
        await SeedAsync("Matched Book", null, author, www: "https://hardcover.app/books/matched");
        await SeedAsync("Unmatched Book", null, author);

        var (books, total) = await _repository.GetStandaloneBooksByAuthorAsync(
            author.Id, limit: 10, offset: 0,
            filter: new BookSummaryFilter(Sources: new[] { "Hardcover" }));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Matched Book", books.Single().BookName);
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

    // ---- Entry-status backing queries ----

    [TestMethod]
    public async Task FindSeriesValueByFoldedNameAsync_AccentAndCaseInsensitive()
    {
        await SeedAsync("Book A", "Études du Cheval");

        var found = await _repository.FindSeriesValueByFoldedNameAsync("etudes du cheval");

        Assert.AreEqual("Études du Cheval", found);
    }

    [TestMethod]
    public async Task FindSeriesValueByFoldedNameAsync_NoSuchSeries_ReturnsNull()
    {
        await SeedAsync("Book A", "Mistborn");

        var found = await _repository.FindSeriesValueByFoldedNameAsync("Stormlight");

        Assert.IsNull(found);
    }

    // Same bounded-prefilter regression as the person version: a candidate sharing only the first
    // token must still surface for the "similar" classification.
    [TestMethod]
    public async Task SearchSeriesValuesAsync_FirstTokenVariant_Surfaces()
    {
        await SeedAsync("Book A", "The Stormlight Archive");
        await SeedAsync("Book B", "The Hormlight Dances");

        var results = await _repository.SearchSeriesValuesAsync("The Storlight Archive", 10);

        CollectionAssert.Contains(results, "The Stormlight Archive");
    }

    // Mirror of the author prefilter's wildcard regression: LIKE wildcards the user types must
    // not leak through to the series prefilter.
    [TestMethod]
    public async Task SearchSeriesValuesAsync_LikeWildcardsInTheQuery_AreTreatedLiterally()
    {
        await SeedAsync("Book A", "The 100% Series");
        await SeedAsync("Book B", "The 100 Series");

        var percent = await _repository.SearchSeriesValuesAsync("100%", 10);

        CollectionAssert.AreEqual(
            new List<string> { "The 100% Series" },
            percent,
            "'%' in the query matches a literal '%', not a wildcard");
    }

    [TestMethod]
    public async Task GetSeriesPartConflictCandidatesAsync_NonNumericParts_CompareCaseInsensitiveTrimmedEquality()
    {
        await SeedAsync("Book A", "Wheel of Time", seriesPart: "Book 1");
        await SeedAsync("Book B", "Wheel of Time", seriesPart: "book 1    ");
        await SeedAsync("Book C", "Wheel of Time", seriesPart: "Book 2");

        var (rows, truncated) = await _repository.GetSeriesPartConflictCandidatesAsync(
            "Wheel of Time", excludeAudiobookId: 999_999, "BOOK 1", limit: 10);

        Assert.IsFalse(truncated);
        CollectionAssert.AreEqual(
            new List<string> { "Book A", "Book B" },
            rows.Select(r => r.BookName).ToList(),
            "non-numeric parts compare trimmed case-insensitively, so 'BOOK 1' conflicts with 'book 1    ' ");
    }

    // Regression guard for the advisory conflict check: the equivalence must be applied in SQL, not
// by taking a bounded alphabetical slice of the series and filtering in memory - a conflict that
// sorts past the bound would be silently missed. Numeric ("1" vs "1.0"), case-insensitive and
// current-book exclusion all have to work against real SQLite.
[TestMethod]
public async Task GetSeriesPartConflictCandidatesAsync_EquivalenceAppliedInSql_ExcludesNonEquivalentAndCurrentBook()
    {
        _defaultAuthor ??= new Person(default, "An Author");
        var current = await _repository.InsertAudiobook(new Audiobook(
            default, "Current", null, "Mistborn", "1", 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/Current.m4b", "Current.m4b", 1000)
        {
            Authors = new List<Person> { _defaultAuthor },
        });

        await SeedAsync("Other A", "Mistborn", seriesPart: "1");
        await SeedAsync("Other B", "Mistborn", seriesPart: "1.0"); // numeric-equivalent
        await SeedAsync("Other C", "Mistborn", seriesPart: "3");    // not equivalent
        await SeedAsync("Other D", "Mistborn", seriesPart: null);   // no part -> never equivalent
        await SeedAsync("Different Series", "Other Series", seriesPart: "1");

        var (rows, truncated) = await _repository.GetSeriesPartConflictCandidatesAsync(
            "Mistborn", current.Id, "1", limit: 10);

        Assert.IsFalse(truncated);
        CollectionAssert.AreEqual(
            new List<string> { "Other A", "Other B" },
            rows.Select(r => r.BookName).ToList(),
            "only parts equivalent to '1' conflict, and the book being edited is always excluded");
        CollectionAssert.DoesNotContain(rows.Select(r => r.AudiobookId).ToList(), current.Id);
    }

[TestMethod]
public async Task GetSeriesPartConflictCandidatesAsync_MoreConflictsThanTheCap_ReportsTruncated()
    {
        await SeedAsync("Alpha", "Mistborn", seriesPart: "1");
        await SeedAsync("Beta", "Mistborn", seriesPart: "1");
        await SeedAsync("Gamma", "Mistborn", seriesPart: "1");
        await SeedAsync("Delta", "Mistborn", seriesPart: "1");

        var (rows, truncated) = await _repository.GetSeriesPartConflictCandidatesAsync(
            "Mistborn", excludeAudiobookId: 999_999, "1", limit: 2);

        Assert.AreEqual(2, rows.Count, "the result is bounded");
        Assert.IsTrue(truncated, "the caller must be told more conflicts exist than the cap carries");
    }
}
