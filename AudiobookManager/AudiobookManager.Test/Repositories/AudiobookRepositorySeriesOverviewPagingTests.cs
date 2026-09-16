using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Paging against real SQLite, because the property under test is one an in-memory list cannot
/// disprove: whether the SQL order is total. A partial ORDER BY lets the database return ties in
/// any order it likes, and two pages taken from different orderings silently repeat one row and
/// drop another. These tests exercise the UNION of distinct audiobook Series tag values and
/// catalog rows that <see cref="AudiobookRepository.GetSeriesValuesPageAsync"/> pages over.
/// </summary>
[TestClass]
public class AudiobookRepositorySeriesOverviewPagingTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;
    private Dictionary<long, Person> _personsByAuthor = new();

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriespaging-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
        _personsByAuthor = new Dictionary<long, Person>();
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

    private static string UniqueAuthorName() => $"An Author {Guid.NewGuid():N}";

    private async Task<Audiobook> SeedBookAsync(string bookName, string series, string? authorName = null)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, authorName ?? UniqueAuthorName()) }
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    private async Task<Series> SeedCatalogRowAsync(string name, bool matched)
    {
        var row = new Series
        {
            Name = name,
            MatchedSourceName = matched ? "Hardcover" : null,
            MatchedSourceId = matched ? $"src-{Guid.NewGuid():N}" : null,
        };
        _db.Series.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private async Task<Audiobook> SeedBookForAuthorAsync(string bookName, string series, long authorId)
    {
        // One Person instance per author, shared across that author's books in this test: the
        // identity map rejects a second instance with the same key value.
        if (!_personsByAuthor.TryGetValue(authorId, out var person))
        {
            _personsByAuthor[authorId] = person = new Person(authorId, $"Author {authorId}");
        }

        var audiobook = new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { person }
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    [TestMethod]
    public async Task GetSeriesValuesPageAsync_ReturnsTheRequestedSliceAndTheFullTotal()
    {
        for (var i = 0; i < 25; i++)
        {
            await SeedBookAsync($"Book {i:02d}", "Series");
            await SeedBookAsync($"Other {i:02d}", $"Serie{i:02d}");
        }

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, null, skip: 10, take: 10);

        Assert.AreEqual(10, items.Count);
        Assert.AreEqual(26, total, "Every distinct series value, catalog included.");
        var sorted = new List<string>(items);
        sorted.Sort(StringComparer.Ordinal);
        Assert.AreSequenceEqual(sorted, items, "Values arrive in a stable order.");
    }

    // A catalog row whose value no longer appears on any audiobook must still be listed - the
    // same contract the unpaged overview it replaces had.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_IncludesCatalogRowsWithNoOwnedBooks()
    {
        await SeedBookAsync("Owned Book", "Mistborn");
        await SeedCatalogRowAsync("Ghost Omnibus", matched: true);

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, null, skip: 0, take: 10);

        Assert.AreEqual(2, total);
        CollectionAssert.Contains(items, "Ghost Omnibus");
    }

    // The union must not double-count a value present both on books and in the catalog.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_UnionsWithoutDuplicatingSharedValues()
    {
        await SeedBookAsync("Owned Book", "Mistborn");
        await SeedCatalogRowAsync("Mistborn", matched: true);

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, null, skip: 0, take: 10);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Mistborn", items.Single());
    }

    // The bug paging invites: every page taken separately, and each name appearing exactly once
    // across all of them.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_PagedRightThrough_CoversEveryValueExactlyOnce()
    {
        for (var i = 0; i < 23; i++)
        {
            await SeedCatalogRowAsync($"Catalog {i:02d}", matched: i % 2 == 0);
            await SeedBookAsync($"Book {i:02d}", $"Book Series {i:02d}");
        }

        var seen = new List<string>();
        for (var page = 0; page < 6; page++)
        {
            var (items, _) = await _repository.GetSeriesValuesPageAsync(null, null, skip: page * 10, take: 10);
            seen.AddRange(items);
        }

        Assert.AreEqual(46, seen.Distinct().Count());
    }

    [TestMethod]
    public async Task GetSeriesValuesPageAsync_SearchFoldsAccents()
    {
        await SeedBookAsync("Book", "Sërîés");
        await SeedBookAsync("Book", "Unrelated");

        var (items, total) = await _repository.GetSeriesValuesPageAsync("serie", null, skip: 0, take: 10);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Sërîés", items.Single());
    }

    [TestMethod]
    public async Task GetSeriesValuesPageAsync_SearchMatchesAuthorNames()
    {
        await SeedBookAsync("Book", "Mistborn", "René Girard");
        await SeedBookAsync("Book", "Other", "Somebody Else");

        var (items, total) = await _repository.GetSeriesValuesPageAsync("rene", null, skip: 0, take: 10);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Mistborn", items.Single());
    }

    // Regression: the paged search interpolated the folded query into the LIKE pattern without
    // escaping, so a literal '%' or '_' the user typed acted as a wildcard.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_LikeWildcardsInTheSearch_AreTreatedLiterally()
    {
        await SeedBookAsync("Book", "B_eta Series");
        await SeedBookAsync("Book", "Breta Series");
        await SeedBookAsync("Book", "100% Series");
        await SeedBookAsync("Book", "100 Friends");

        var (underscore, totalUnderscore) = await _repository.GetSeriesValuesPageAsync("B_eta", null, skip: 0, take: 10);
        CollectionAssert.AreEqual(
            new List<string> { "B_eta Series" },
            underscore,
            "an underscore in the search matches a literal underscore, not 'any character'");
        Assert.AreEqual(1, totalUnderscore);

        var (percent, totalPercent) = await _repository.GetSeriesValuesPageAsync("100%", null, skip: 0, take: 10);
        CollectionAssert.AreEqual(
            new List<string> { "100% Series" },
            percent,
            "'%' in the search matches a literal '%', not a wildcard");
        Assert.AreEqual(1, totalPercent);
    }

    [TestMethod]
    public async Task GetSeriesValuesPageAsync_MatchedFilter_IncludesOnlyCatalogMatchedValues()
    {
        await SeedBookAsync("Book", "MatchedSeries");
        await SeedCatalogRowAsync("MatchedSeries", matched: true);
        await SeedBookAsync("Book", "PlainValue");
        await SeedCatalogRowAsync("PlainValue", matched: false);

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, true, skip: 0, take: 10);

        Assert.AreEqual(1, total);
        Assert.AreEqual("MatchedSeries", items.Single());
    }

    [TestMethod]
    public async Task GetSeriesValuesPageAsync_UnmatchedFilter_ExcludesCatalogMatchedValues()
    {
        await SeedBookAsync("Book", "MatchedSeries");
        await SeedCatalogRowAsync("MatchedSeries", matched: true);
        await SeedBookAsync("Book", "PlainValue");
        await SeedCatalogRowAsync("PlainValue", matched: false);

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, false, skip: 0, take: 10);

        Assert.AreEqual(1, total);
        Assert.AreEqual("PlainValue", items.Single());
    }

    // The author detail's series section is scoped to one author: only the distinct series
    // values of books that author actually owns in. Catalog rows the author owns nothing in -
    // and series other authors own - must not appear.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_AuthorFilter_ReturnsOnlyTheSeriesThatAuthorOwnsBooksIn()
    {
        var authorA = (await SeedBookForAuthorAsync("Book A1", "Author A Series", 1)).Authors.Single().Id;
        await SeedBookForAuthorAsync("Book A2", "Author A Series", 1);
        await SeedBookForAuthorAsync("Book Shared", "Shared Series", 1);
        await SeedBookForAuthorAsync("Other Author's Book", "Other Series", 2);
        await SeedCatalogRowAsync("Catalog Only", matched: true);

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, null, skip: 0, take: 10, authorId: authorA);

        Assert.AreEqual(2, total);
        CollectionAssert.AreEquivalent(
            new[] { "Author A Series", "Shared Series" },
            items,
            "the author's own distinct series values, and nothing the author owns no book in");
    }

    // An author scope never unions catalog rows: a catalog-only value is not a series the
    // author has, even though the whole-library page deliberately lists it.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_AuthorFilter_DoesNotIncludeCatalogOnlyRows()
    {
        await SeedBookForAuthorAsync("Book", "Owned Series", 1);
        await SeedCatalogRowAsync("Ghost Catalog Series", matched: true);

        var (items, total) = await _repository.GetSeriesValuesPageAsync(null, null, skip: 0, take: 10, authorId: 1);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Owned Series", items.Single());
    }

    // The author filter composes with the accent-folding search: a "etern" query still finds the
    // author's "Sërîés Éternal" series via the series value.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_AuthorFilter_KeepsAccentFoldingSearch()
    {
        await SeedBookForAuthorAsync("Book", "Sërîés Éternal", 1);
        await SeedBookForAuthorAsync("Book", "Unrelated", 1);

        var (items, total) = await _repository.GetSeriesValuesPageAsync("etern", null, skip: 0, take: 10, authorId: 1);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Sërîés Éternal", items.Single());
    }

    // An author scope stays stable across pages like the whole-library union does: the distinct
    // series values are unique, so ordering by the value alone keeps every page stable.
    [TestMethod]
    public async Task GetSeriesValuesPageAsync_AuthorFilter_PagedRightThrough_CoversEveryValueExactlyOnce()
    {
        for (var i = 0; i < 23; i++)
        {
            await SeedBookForAuthorAsync($"Book {i:02d}", $"Author Series {i:02d}", 1);
        }
        await SeedBookForAuthorAsync("Book", "Other Series", 2);

        var seen = new List<string>();
        for (var page = 0; page < 3; page++)
        {
            var (items, _) = await _repository.GetSeriesValuesPageAsync(null, null, skip: page * 10, take: 10, authorId: 1);
            seen.AddRange(items);
        }

        Assert.AreEqual(23, seen.Count, "all 23 of the author's series values, no other author's");
        Assert.AreEqual(23, seen.Distinct().Count(), "no series value may appear on two pages");
    }

    [TestMethod]
    public async Task GetSeriesValueCountsAsync_CountsEachBucketSeparately()
    {
        await SeedBookAsync("Book", "MatchedSeries");
        await SeedCatalogRowAsync("MatchedSeries", matched: true);
        await SeedBookAsync("Book", "UnmatchedValue");
        await SeedCatalogRowAsync("CatalogOnly", matched: true);

        var (total, matched) = await _repository.GetSeriesValueCountsAsync();

        Assert.AreEqual(3, total);
        Assert.AreEqual(2, matched, "Matched counts catalog rows with a source, book-backed or not.");
    }

    [TestMethod]
    public async Task GetSeriesGroupingDataAsync_BoundedToTheGivenValues()
    {
        var relevant = await SeedBookAsync("Book", "KeepMe");
        await SeedBookAsync("Book", "DropMe");

        var rows = await _repository.GetSeriesGroupingDataAsync(new List<string> { "KeepMe" });

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("KeepMe", rows[0].Series);
        Assert.AreEqual(1, rows[0].Authors.Count);
    }

    // The similar-series detection shows a book count per candidate; the count query must be
    // scoped to the values it is asked about, and must group per series value.
    [TestMethod]
    public async Task GetSeriesBookCountsAsync_CountsOnlyTheRequestedValues()
    {
        await SeedBookAsync("Book One", "Mistborn");
        await SeedBookAsync("Book Two", "Mistborn");
        await SeedBookAsync("Book Three", "Stormlight Archive");
        await SeedBookAsync("Book Four", "Untouched");

        var counts = await _repository.GetSeriesBookCountsAsync(new List<string> { "Mistborn", "Stormlight Archive" });

        Assert.AreEqual(2, counts.Count);
        Assert.AreEqual(2, counts["Mistborn"]);
        Assert.AreEqual(1, counts["Stormlight Archive"]);
    }

    [TestMethod]
    public async Task GetSeriesBookCountsAsync_EmptyInputRequiresNoBookRows()
    {
        await SeedBookAsync("Book One", "Mistborn");

        var counts = await _repository.GetSeriesBookCountsAsync(new List<string>());

        Assert.AreEqual(0, counts.Count);
    }
}