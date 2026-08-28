using AudiobookManager.Database;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the persisted per-UTC-day Hardcover request counter (see the "Hardcover request
/// quota" section of CLAUDE.md) against a real (temp-file) SQLite database, since the point of
/// this table is exactly that the count survives across separate DbContext instances/process
/// restarts rather than resetting mid-day.
/// </summary>
[TestClass]
public class HardcoverQuotaRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private HardcoverQuotaRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hardcoverquotarepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new HardcoverQuotaRepository(_db);
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

    private DatabaseContext OpenNewContext()
    {
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        return new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
    }

    [TestMethod]
    public async Task GetCountAsync_NoRowForDate_ReturnsZero()
    {
        var count = await _repository.GetCountAsync(new DateOnly(2026, 1, 1));

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task TryConsumeAsync_FirstCallForADay_CreatesRowWithCountOne()
    {
        var date = new DateOnly(2026, 1, 1);

        var consumed = await _repository.TryConsumeAsync(date, dailyLimit: 5);

        Assert.IsTrue(consumed);
        Assert.AreEqual(1, await _repository.GetCountAsync(date));
    }

    [TestMethod]
    public async Task TryConsumeAsync_SubsequentCalls_IncrementTheSameDayRow()
    {
        var date = new DateOnly(2026, 1, 1);

        await _repository.TryConsumeAsync(date, dailyLimit: 5);
        await _repository.TryConsumeAsync(date, dailyLimit: 5);
        var third = await _repository.TryConsumeAsync(date, dailyLimit: 5);

        Assert.IsTrue(third);
        Assert.AreEqual(3, await _repository.GetCountAsync(date));
    }

    [TestMethod]
    public async Task TryConsumeAsync_AtDailyLimit_ReturnsFalseAndDoesNotIncrement()
    {
        var date = new DateOnly(2026, 1, 1);

        await _repository.TryConsumeAsync(date, dailyLimit: 2);
        await _repository.TryConsumeAsync(date, dailyLimit: 2);
        var thirdConsumed = await _repository.TryConsumeAsync(date, dailyLimit: 2);

        Assert.IsFalse(thirdConsumed);
        Assert.AreEqual(2, await _repository.GetCountAsync(date));
    }

    [TestMethod]
    public async Task TryConsumeAsync_ZeroDailyLimitOnFirstCallForADay_ReturnsFalseWithoutCreatingRow()
    {
        var date = new DateOnly(2026, 1, 1);

        var consumed = await _repository.TryConsumeAsync(date, dailyLimit: 0);

        Assert.IsFalse(consumed);
        Assert.AreEqual(0, await _repository.GetCountAsync(date));
    }

    [TestMethod]
    public async Task TryConsumeAsync_DifferentDates_TrackIndependentCounters()
    {
        var day1 = new DateOnly(2026, 1, 1);
        var day2 = new DateOnly(2026, 1, 2);

        await _repository.TryConsumeAsync(day1, dailyLimit: 5);
        await _repository.TryConsumeAsync(day1, dailyLimit: 5);
        await _repository.TryConsumeAsync(day2, dailyLimit: 5);

        Assert.AreEqual(2, await _repository.GetCountAsync(day1));
        Assert.AreEqual(1, await _repository.GetCountAsync(day2));
    }

    [TestMethod]
    public async Task TryConsumeAsync_CountPersistsAcrossSeparateDbContextInstances()
    {
        var date = new DateOnly(2026, 1, 1);

        await _repository.TryConsumeAsync(date, dailyLimit: 5);
        await _repository.TryConsumeAsync(date, dailyLimit: 5);

        using var freshContext = OpenNewContext();
        var freshRepository = new HardcoverQuotaRepository(freshContext);

        Assert.AreEqual(2, await freshRepository.GetCountAsync(date));

        var consumed = await freshRepository.TryConsumeAsync(date, dailyLimit: 5);
        Assert.IsTrue(consumed);
        Assert.AreEqual(3, await freshRepository.GetCountAsync(date));
    }

    [TestMethod]
    public async Task TryConsumeAsync_ConcurrentCallers_NeverExceedTheDailyLimit()
    {
        // Regression: the counter used a read-modify-write in C#, so two callers could both read
        // the same count and write count+1 - a lost update that silently overran the daily budget.
        // Hardcover requests really are concurrent (SearchMultiple fans out across sources, and
        // the retry handler re-enters), so the compare-and-increment has to be one SQL statement.
        const int dailyLimit = 25;
        const int attempts = 100;
        var date = new DateOnly(2026, 3, 1);

        // Each caller gets its own DbContext, the way a per-request DI scope would.
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        var contexts = new List<DatabaseContext>();
        try
        {
            var tasks = new List<Task<bool>>();
            for (var i = 0; i < attempts; i++)
            {
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new HardcoverQuotaRepository(context);
                tasks.Add(Task.Run(() => repository.TryConsumeAsync(date, dailyLimit)));
            }

            var results = await Task.WhenAll(tasks);

            Assert.AreEqual(dailyLimit, results.Count(granted => granted),
                "exactly the daily limit worth of requests should have been granted");
            Assert.AreEqual(dailyLimit, await _repository.GetCountAsync(date),
                "the persisted counter must match the number of grants");
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task TryConsumeAsync_SameRepositoryInstance_ReportsTheDatabaseCountNotAStaleTrackedRow()
    {
        // The increment is a set-based ExecuteUpdate, which bypasses the change tracker: a row
        // left tracked by the insert path would keep reporting the count it had at insert time.
        var date = new DateOnly(2026, 3, 2);

        await _repository.TryConsumeAsync(date, 10);
        await _repository.TryConsumeAsync(date, 10);
        await _repository.TryConsumeAsync(date, 10);

        Assert.AreEqual(3, await _repository.GetCountAsync(date));
    }
}
