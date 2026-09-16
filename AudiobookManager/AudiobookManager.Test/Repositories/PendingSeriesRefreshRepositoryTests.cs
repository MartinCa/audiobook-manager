using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The pending series-refresh store, exercised against a real (temp-file) SQLite database - the
/// per-series uniqueness, the one-row-per-series upsert (a fresh refresh supersedes the old
/// snapshot), the delete, and the bounded page ordering are all database behavior.
/// </summary>
[TestClass]
public class PendingSeriesRefreshRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PendingSeriesRefreshRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pendseriesrefresh-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PendingSeriesRefreshRepository(_db);
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

    private static PendingSeriesRefresh Row(string seriesName, DateTime fetchedAt) => new()
    {
        SeriesName = seriesName,
        FetchedAt = fetchedAt,
        SourceName = "Hardcover",
        SourceUrl = $"https://hardcover.app/series/{seriesName}",
        PayloadJson = "{}",
    };

    [TestMethod]
    public async Task UpsertAsync_InsertsOneRowPerSeries()
    {
        await _repository.UpsertAsync(Row("Mistborn", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)));

        var stored = await _repository.GetBySeriesNameAsync("Mistborn");
        Assert.IsNotNull(stored);
        Assert.AreEqual("Mistborn", stored.SeriesName);
        Assert.AreEqual("{}", stored.PayloadJson);
    }

    [TestMethod]
    public async Task UpsertAsync_AFreshRefreshSupersedesThePreviousSnapshot()
    {
        await _repository.UpsertAsync(Row("Mistborn", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)));
        var superseding = Row("Mistborn", new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc));
        superseding.PayloadJson = "{\"new\":true}";
        await _repository.UpsertAsync(superseding);

        var stored = await _repository.GetBySeriesNameAsync("Mistborn");
        Assert.IsNotNull(stored);
        Assert.AreEqual(new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), stored.FetchedAt);
        Assert.AreEqual("{\"new\":true}", stored.PayloadJson);
        Assert.AreEqual(1, await _repository.CountAsync());
    }

    [TestMethod]
    public async Task GetBySeriesNameAsync_MissingSeries_ReturnsNull()
    {
        Assert.IsNull(await _repository.GetBySeriesNameAsync("Mistborn"));
    }

    [TestMethod]
    public async Task DeleteBySeriesNameAsync_ReturnsWhetherARowExisted()
    {
        await _repository.UpsertAsync(Row("Mistborn", DateTime.UtcNow));

        Assert.IsTrue(await _repository.DeleteBySeriesNameAsync("Mistborn"));
        Assert.IsFalse(await _repository.DeleteBySeriesNameAsync("Mistborn"));
        Assert.IsNull(await _repository.GetBySeriesNameAsync("Mistborn"));
    }

    [TestMethod]
    public async Task GetPageAsync_OrdersNewestFetchFirst_AndPages()
    {
        await _repository.UpsertAsync(Row("Oldest", new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)));
        await _repository.UpsertAsync(Row("Newest", new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc)));
        await _repository.UpsertAsync(Row("Middle", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)));

        var (firstPage, total) = await _repository.GetPageAsync(0, 2);
        Assert.AreEqual(3, total);
        Assert.AreEqual(2, firstPage.Count);
        Assert.AreEqual("Newest", firstPage[0].SeriesName);
        Assert.AreEqual("Middle", firstPage[1].SeriesName);

        var (secondPage, _) = await _repository.GetPageAsync(2, 2);
        Assert.AreEqual(1, secondPage.Count);
        Assert.AreEqual("Oldest", secondPage[0].SeriesName);
    }

    [TestMethod]
    public async Task GetPageAsync_IsTotalOrdered_WhenFetchedAtTies()
    {
        var stamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        await _repository.UpsertAsync(Row("A", stamp));
        await _repository.UpsertAsync(Row("B", stamp));

        var (items, total) = await _repository.GetPageAsync(0, 1);
        Assert.AreEqual(2, total);
        // The ThenBy(Id) tiebreaker makes the page deterministic: the same row cannot appear on
        // two pages (the paged-query total-order rule).
        Assert.AreEqual(1, items.Count);
        Assert.IsTrue(items[0].SeriesName is "A" or "B");

        var first = items[0].SeriesName;
        var (secondPage, _) = await _repository.GetPageAsync(1, 1);
        Assert.AreNotEqual(first, secondPage[0].SeriesName);
    }
}