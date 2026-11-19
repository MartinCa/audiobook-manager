using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The user-chosen sort of the authors, series and book lists. Real SQLite, because the point is
/// the translated ORDER BY and the paging on top of it, not the LINQ.
/// </summary>
[TestClass]
public class ListSortingTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _books = null!;
    private PersonRepository _people = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"listsorting-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _books = new AudiobookRepository(_db);
        _people = new PersonRepository(_db);
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

    private readonly Dictionary<string, Person> _authors = new();

    private async Task<Audiobook> SeedAsync(
        string title, string author, int year = 2020, string? series = null, string? part = null, int? duration = null)
    {
        if (!_authors.TryGetValue(author, out var person))
        {
            person = _authors[author] = new Person(default, author);
        }

        var book = new Audiobook(
            default, title, null, series, part, year,
            null, null, null, null, null, null, null, null, duration,
            $"/library/{title}.m4b", $"{title}.m4b", 1000)
        {
            Authors = new List<Person> { person },
        };
        return await _books.InsertAudiobook(book);
    }

    private static List<string> Titles(IEnumerable<Audiobook> books) => books.Select(b => b.BookName).ToList();

    // ---- Books ------------------------------------------------------------------------------

    [TestMethod]
    public async Task Books_ByYear_OrdersBothWays_AndBreaksTiesOnTitle()
    {
        await SeedAsync("B", "X", year: 2010);
        await SeedAsync("A", "X", year: 2010);
        await SeedAsync("C", "X", year: 2001);

        var (asc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.Year, false));
        var (desc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.Year, true));

        CollectionAssert.AreEqual(new[] { "C", "A", "B" }, Titles(asc));
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Titles(desc));
    }

    [TestMethod]
    public async Task Books_ByDuration_PutsBooksWithoutOneFirstAscendingAndLastDescending()
    {
        await SeedAsync("Long", "X", duration: 9000);
        await SeedAsync("Unknown", "X", duration: null);
        await SeedAsync("Short", "X", duration: 60);

        var (asc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.Duration, false));
        var (desc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.Duration, true));

        CollectionAssert.AreEqual(new[] { "Unknown", "Short", "Long" }, Titles(asc));
        CollectionAssert.AreEqual(new[] { "Long", "Short", "Unknown" }, Titles(desc));
    }

    [TestMethod]
    public async Task Books_ByLastRefreshed_OrdersByTheRefreshTimestamp()
    {
        var old = await SeedAsync("Old", "X");
        var recent = await SeedAsync("Recent", "X");
        await SeedAsync("Never", "X");
        await _db.Audiobooks.Where(a => a.Id == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastMetadataRefreshedAt, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _db.Audiobooks.Where(a => a.Id == recent.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastMetadataRefreshedAt, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var (desc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.LastRefreshed, true));

        CollectionAssert.AreEqual(new[] { "Recent", "Old", "Never" }, Titles(desc));
    }

    [TestMethod]
    public async Task Books_BySeries_GroupsBySeriesInPartOrder_WithSeriesLessBooksLastInBothDirections()
    {
        await SeedAsync("Standalone", "X");
        await SeedAsync("Beta 2", "X", series: "Beta", part: "2");
        await SeedAsync("Alpha 10", "X", series: "Alpha", part: "10");
        await SeedAsync("Alpha 2", "X", series: "Alpha", part: "2");

        var (asc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.Series, false));
        var (desc, _) = await _books.GetAllAsync(10, 0, null, new BookSort(BookSortKey.Series, true));

        CollectionAssert.AreEqual(new[] { "Alpha 2", "Alpha 10", "Beta 2", "Standalone" }, Titles(asc));
        CollectionAssert.AreEqual(new[] { "Beta 2", "Alpha 10", "Alpha 2", "Standalone" }, Titles(desc));
    }

    [TestMethod]
    public async Task Books_SortKeepsTheFiltersAndPagesWithoutRepeatingOrSkipping()
    {
        for (var i = 0; i < 6; i++)
        {
            await SeedAsync($"Book {i}", "X", year: 2000 + (i % 2), duration: i < 4 ? 100 : 5);
        }

        var filter = new BookSummaryFilter(MinDurationInSeconds: 50);
        var sort = new BookSort(BookSortKey.Year, true);
        var (first, total) = await _books.GetAllAsync(2, 0, filter, sort);
        var (second, _) = await _books.GetAllAsync(2, 2, filter, sort);

        Assert.AreEqual(4, total);
        CollectionAssert.AreEqual(new[] { "Book 1", "Book 3", "Book 0", "Book 2" }, Titles(first.Concat(second)));
    }

    [TestMethod]
    public async Task Books_SearchWithASort_UsesTheSortInsteadOfRelevance()
    {
        await SeedAsync("Dune 2", "X", year: 2005);
        await SeedAsync("Dune 1", "X", year: 1999);

        var (byYear, _) = await _books.SearchAsync("dune", 10, 0, sort: new BookSort(BookSortKey.Year, false));

        CollectionAssert.AreEqual(new[] { "Dune 1", "Dune 2" }, Titles(byYear));
    }

    // ---- Authors ----------------------------------------------------------------------------

    private async Task SeedAuthorsWithBookCountsAsync()
    {
        // Few: 1 book, Many: 3 books, Mid: 2 books.
        await SeedAsync("f1", "Few");
        await SeedAsync("m1", "Many");
        await SeedAsync("m2", "Many");
        await SeedAsync("m3", "Many");
        await SeedAsync("d1", "Mid");
        await SeedAsync("d2", "Mid");
    }

    private static List<string> Names(IEnumerable<AuthorSummaryRow> rows) => rows.Select(r => r.Name).ToList();

    [TestMethod]
    public async Task Authors_ByBookCount_OrdersBothWays()
    {
        await SeedAuthorsWithBookCountsAsync();

        var (asc, _) = await _people.GetAuthorSummariesPagedAsync(null, 10, 0, sort: new AuthorSort(AuthorSortKey.BookCount, false));
        var (desc, _) = await _people.GetAuthorSummariesPagedAsync(null, 10, 0, sort: new AuthorSort(AuthorSortKey.BookCount, true));

        CollectionAssert.AreEqual(new[] { "Few", "Mid", "Many" }, Names(asc));
        CollectionAssert.AreEqual(new[] { "Many", "Mid", "Few" }, Names(desc));
    }

    [TestMethod]
    public async Task Authors_ByBookCount_StillAppliesFiltersAndPaging()
    {
        await SeedAuthorsWithBookCountsAsync();

        var sort = new AuthorSort(AuthorSortKey.BookCount, true);
        var filter = new AuthorSummaryFilter(MinBookCount: 2);
        var (page1, total) = await _people.GetAuthorSummariesPagedAsync(null, 1, 0, filter, sort: sort);
        var (page2, _) = await _people.GetAuthorSummariesPagedAsync(null, 1, 1, filter, sort: sort);

        Assert.AreEqual(2, total);
        CollectionAssert.AreEqual(new[] { "Many", "Mid" }, Names(page1.Concat(page2)));
    }

    [TestMethod]
    public async Task Authors_NameDescending_ReversesTheDefaultOrder()
    {
        await SeedAuthorsWithBookCountsAsync();

        var (desc, _) = await _people.GetAuthorSummariesPagedAsync(null, 10, 0, sort: new AuthorSort(AuthorSortKey.Name, true));

        CollectionAssert.AreEqual(new[] { "Mid", "Many", "Few" }, Names(desc));
    }

    [TestMethod]
    public async Task Authors_ByLastRefreshed_PutsNeverRefreshedFirstAscending()
    {
        await SeedAuthorsWithBookCountsAsync();
        await _db.Persons.Where(p => p.Name == "Few")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRefreshedAt, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _db.Persons.Where(p => p.Name == "Mid")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRefreshedAt, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        var (asc, _) = await _people.GetAuthorSummariesPagedAsync(null, 10, 0, sort: new AuthorSort(AuthorSortKey.LastRefreshed, false));
        var (desc, _) = await _people.GetAuthorSummariesPagedAsync(null, 10, 0, sort: new AuthorSort(AuthorSortKey.LastRefreshed, true));

        CollectionAssert.AreEqual(new[] { "Many", "Mid", "Few" }, Names(asc));
        CollectionAssert.AreEqual(new[] { "Few", "Mid", "Many" }, Names(desc));
    }

    [TestMethod]
    public async Task Authors_ByMissingBooks_OrdersByTheSuppliedCounts_AndPages()
    {
        await SeedAuthorsWithBookCountsAsync();
        var ids = await _db.Persons.ToDictionaryAsync(p => p.Name, p => p.Id);
        var counts = new Dictionary<long, int> { [ids["Few"]] = 5, [ids["Mid"]] = 1 };

        var sort = new AuthorSort(AuthorSortKey.MissingBooks, true);
        var (page1, total) = await _people.GetAuthorSummariesPagedAsync(null, 2, 0, sort: sort, sortCounts: counts);
        var (page2, _) = await _people.GetAuthorSummariesPagedAsync(null, 2, 2, sort: sort, sortCounts: counts);
        var (asc, _) = await _people.GetAuthorSummariesPagedAsync(
            null, 10, 0, sort: new AuthorSort(AuthorSortKey.MissingBooks, false), sortCounts: counts);

        Assert.AreEqual(3, total);
        CollectionAssert.AreEqual(new[] { "Few", "Mid", "Many" }, Names(page1.Concat(page2)));
        CollectionAssert.AreEqual(new[] { "Many", "Mid", "Few" }, Names(asc));
    }

    [TestMethod]
    public async Task Authors_ByMissingBooksWithoutCounts_FallsBackToTheDefaultOrder()
    {
        await SeedAuthorsWithBookCountsAsync();

        var (rows, _) = await _people.GetAuthorSummariesPagedAsync(
            null, 10, 0, sort: new AuthorSort(AuthorSortKey.MissingBooks, true), sortCounts: null);

        CollectionAssert.AreEqual(new[] { "Few", "Many", "Mid" }, Names(rows));
    }

    // ---- Series -----------------------------------------------------------------------------

    private async Task SeedSeriesAsync()
    {
        // Alpha: 1 book, Beta: 3 books, Gamma: 2 books, plus a catalog-only series with none.
        await SeedAsync("a1", "X", series: "Alpha", part: "1");
        await SeedAsync("b1", "X", series: "Beta", part: "1");
        await SeedAsync("b2", "X", series: "Beta", part: "2");
        await SeedAsync("b3", "X", series: "Beta", part: "3");
        await SeedAsync("g1", "X", series: "Gamma", part: "1");
        await SeedAsync("g2", "X", series: "Gamma", part: "2");
        _db.Series.Add(new Series { Name = "Delta", LastRefreshedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Series_ByBookCount_OrdersBothWays_CountingACatalogOnlySeriesAsZero()
    {
        await SeedSeriesAsync();

        var (asc, total) = await _books.GetSeriesValuesPageAsync(null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.BookCount, false));
        var (desc, _) = await _books.GetSeriesValuesPageAsync(null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.BookCount, true));

        Assert.AreEqual(4, total);
        CollectionAssert.AreEqual(new[] { "Delta", "Alpha", "Gamma", "Beta" }, asc);
        CollectionAssert.AreEqual(new[] { "Beta", "Gamma", "Alpha", "Delta" }, desc);
    }

    [TestMethod]
    public async Task Series_ByBookCount_PagesAndKeepsFilters()
    {
        await SeedSeriesAsync();

        var sort = new SeriesSort(SeriesSortKey.BookCount, true);
        var filter = new SeriesOverviewFilter(MinOwnedBooks: 2);
        var (page1, total) = await _books.GetSeriesValuesPageAsync(null, null, 0, 1, filter: filter, sort: sort);
        var (page2, _) = await _books.GetSeriesValuesPageAsync(null, null, 1, 1, filter: filter, sort: sort);

        Assert.AreEqual(2, total);
        CollectionAssert.AreEqual(new[] { "Beta", "Gamma" }, page1.Concat(page2).ToList());
    }

    [TestMethod]
    public async Task Series_ByLastRefreshed_SortsSeriesWithoutARefreshLast()
    {
        await SeedSeriesAsync();
        await _db.Series.AddAsync(new Series { Name = "Alpha", LastRefreshedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await _db.SaveChangesAsync();

        var (asc, _) = await _books.GetSeriesValuesPageAsync(null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.LastRefreshed, false));
        var (desc, _) = await _books.GetSeriesValuesPageAsync(null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.LastRefreshed, true));

        CollectionAssert.AreEqual(new[] { "Alpha", "Delta", "Beta", "Gamma" }, asc);
        CollectionAssert.AreEqual(new[] { "Delta", "Alpha", "Beta", "Gamma" }, desc);
    }

    [TestMethod]
    public async Task Series_BySuppliedValues_OrdersByThemAndSortsSeriesWithoutOneLast()
    {
        await SeedSeriesAsync();
        var values = new Dictionary<string, double> { ["Alpha"] = 0.5, ["Gamma"] = 1.0, ["Beta"] = 0.1 };

        var (asc, _) = await _books.GetSeriesValuesPageAsync(
            null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.Completion, false), sortValues: values);
        var (desc, _) = await _books.GetSeriesValuesPageAsync(
            null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.Completion, true), sortValues: values);

        CollectionAssert.AreEqual(new[] { "Beta", "Alpha", "Gamma", "Delta" }, asc);
        CollectionAssert.AreEqual(new[] { "Gamma", "Alpha", "Beta", "Delta" }, desc);
    }

    [TestMethod]
    public async Task Series_NameDescending_ReversesTheDefaultOrder()
    {
        await SeedSeriesAsync();

        var (desc, _) = await _books.GetSeriesValuesPageAsync(null, null, 0, 10, sort: new SeriesSort(SeriesSortKey.Name, true));

        CollectionAssert.AreEqual(new[] { "Gamma", "Delta", "Beta", "Alpha" }, desc);
    }

    [TestMethod]
    public async Task Series_NameAscending_KeepsTheDefaultSqlPagedOrder()
    {
        await SeedSeriesAsync();

        var (asc, total) = await _books.GetSeriesValuesPageAsync(null, null, 1, 2, sort: new SeriesSort(SeriesSortKey.Name, false));

        Assert.AreEqual(4, total);
        CollectionAssert.AreEqual(new[] { "Beta", "Delta" }, asc);
    }

    // ---- Parser -----------------------------------------------------------------------------

    [TestMethod]
    public void Parser_NoSortBy_IsNoSort_EvenWithADirection()
    {
        Assert.IsTrue(ListSortParser.TryParse<BookSortKey>(null, "desc", out var sort, out var error));
        Assert.IsNull(sort);
        Assert.IsNull(error);
    }

    [TestMethod]
    public void Parser_ReadsCamelCaseKeysAndDirectionsCaseInsensitively()
    {
        Assert.IsTrue(ListSortParser.TryParse<AuthorSortKey>("bookCount", "DESC", out var sort, out _));
        Assert.AreEqual((AuthorSortKey.BookCount, true), sort);

        Assert.IsTrue(ListSortParser.TryParse<AuthorSortKey>("LastRefreshed", null, out sort, out _));
        Assert.AreEqual((AuthorSortKey.LastRefreshed, false), sort);
    }

    [TestMethod]
    [DataRow("nope", "asc")]
    [DataRow("1", "asc")]
    [DataRow("name,bookCount", "asc")]
    [DataRow("name", "sideways")]
    public void Parser_RejectsUnknownKeysAndDirections(string sortBy, string sortDir)
    {
        Assert.IsFalse(ListSortParser.TryParse<AuthorSortKey>(sortBy, sortDir, out var sort, out var error));
        Assert.IsNull(sort);
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }
}
