using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Against real SQLite, like <see cref="BookConsistencyIssueRepositoryPagingTests"/>: the
/// unique-per-series upsert and the ordering both depend on behavior an in-memory fake cannot
/// disprove.
/// </summary>
[TestClass]
public class SeriesConsistencyIssueRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private SeriesConsistencyIssueRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesconsistency-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new SeriesConsistencyIssueRepository(_db);
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

    private async Task<Series> SeedSeriesAsync(string name)
    {
        var series = new Series { Name = name };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();
        return series;
    }

    [TestMethod]
    public async Task UpsertFailureAsync_CalledTwiceForTheSameSeries_ReplacesRatherThanDuplicates()
    {
        var series = await SeedSeriesAsync("Mistborn");

        await _repository.UpsertFailureAsync(series.Id, "first error");
        await _repository.UpsertFailureAsync(series.Id, "second error");

        var (items, totalCount) = await _repository.GetPageWithSeriesAsync(0, 10);

        Assert.AreEqual(1, totalCount);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("second error", items[0].ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteBySeriesIdAsync_RemovesTheRow()
    {
        var series = await SeedSeriesAsync("Mistborn");
        await _repository.UpsertFailureAsync(series.Id, "an error");

        await _repository.DeleteBySeriesIdAsync(series.Id);

        var (items, totalCount) = await _repository.GetPageWithSeriesAsync(0, 10);
        Assert.AreEqual(0, totalCount);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public async Task DeleteBySeriesIdAsync_NoExistingRow_IsANoOp()
    {
        var series = await SeedSeriesAsync("Mistborn");

        await _repository.DeleteBySeriesIdAsync(series.Id);

        var (_, totalCount) = await _repository.GetPageWithSeriesAsync(0, 10);
        Assert.AreEqual(0, totalCount);
    }

    // Regression: series_id is unique and UpsertFailureAsync reads before it inserts, across an
    // await on a request-scoped context. A single refresh and the fire-and-forget bulk sweep can
    // both fail the same series concurrently, both find the row missing, and both insert - the
    // loser used to fail with a raw "UNIQUE constraint failed" instead of recording its error.
    [TestMethod]
    public async Task UpsertFailureAsync_ConcurrentFirstWrites_AllSucceedAndCreateOneRow()
    {
        var series = await SeedSeriesAsync("Mistborn");
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                // A context per caller, as each request scope gets its own.
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new SeriesConsistencyIssueRepository(context);
                var errorMessage = $"error {i}";
                calls.Add(Task.Run(() => repository.UpsertFailureAsync(series.Id, errorMessage)));
            }

            await Task.WhenAll(calls);

            var rows = await _db.SeriesConsistencyIssues.AsNoTracking()
                .Where(i => i.SeriesId == series.Id).ToListAsync();
            Assert.AreEqual(1, rows.Count);
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task GetPageWithSeriesAsync_OrdersNewestFirst_AndIncludesTheSeries()
    {
        var older = await SeedSeriesAsync("Mistborn");
        var newer = await SeedSeriesAsync("Wax and Wayne");

        await _repository.UpsertFailureAsync(older.Id, "older error");
        await Task.Delay(10);
        await _repository.UpsertFailureAsync(newer.Id, "newer error");

        var (items, totalCount) = await _repository.GetPageWithSeriesAsync(0, 10);

        Assert.AreEqual(2, totalCount);
        Assert.AreEqual("Wax and Wayne", items[0].Series.Name);
        Assert.AreEqual("Mistborn", items[1].Series.Name);
    }
}
