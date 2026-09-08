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

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriespaging-{Guid.NewGuid():N}.db");
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