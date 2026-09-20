using AudiobookManager.Database;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the per-task-key scheduled task run table against a real (temp-file) SQLite
/// database: the interesting behavior is the upsert-by-key and the get-or-create race handling,
/// both of which need an actual database to mean anything.
/// </summary>
[TestClass]
public class ScheduledTaskRunRepositoryTests
{
    private const string TaskKey = "upcoming_releases_refresh";

    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private ScheduledTaskRunRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"scheduledtaskrunrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new ScheduledTaskRunRepository(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
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
    public async Task GetAsync_NoRowYet_ReturnsNull()
    {
        var run = await _repository.GetAsync(TaskKey);

        Assert.IsNull(run);
    }

    [TestMethod]
    public async Task RecordRunAsync_NoRowYet_CreatesRowWithTheGivenOutcome()
    {
        var startedAt = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);

        await _repository.RecordRunAsync(TaskKey, startedAt, 1234, succeeded: true, error: null);

        using var freshContext = OpenNewContext();
        var run = await freshContext.ScheduledTaskRuns.SingleAsync();
        Assert.AreEqual(TaskKey, run.TaskKey);
        Assert.AreEqual(startedAt, run.LastRunStartedAt);
        Assert.AreEqual(1234, run.LastRunDurationMs);
        Assert.AreEqual("Success", run.LastRunStatus);
        Assert.IsNull(run.LastRunError);
    }

    [TestMethod]
    public async Task RecordRunAsync_FailedRun_StoresFailedStatusAndTheErrorMessage()
    {
        await _repository.RecordRunAsync(
            TaskKey, DateTime.UtcNow, 500, succeeded: false, error: "Hardcover request failed");

        var run = await _repository.GetAsync(TaskKey);

        Assert.IsNotNull(run);
        Assert.AreEqual("Failed", run!.LastRunStatus);
        Assert.AreEqual("Hardcover request failed", run.LastRunError);
    }

    [TestMethod]
    public async Task RecordRunAsync_RowExists_OverwritesItInPlaceRatherThanKeepingHistory()
    {
        await _repository.RecordRunAsync(TaskKey, DateTime.UtcNow, 100, succeeded: false, error: "boom");
        await _repository.RecordRunAsync(TaskKey, DateTime.UtcNow, 200, succeeded: true, error: null);

        using var freshContext = OpenNewContext();
        Assert.AreEqual(1, await freshContext.ScheduledTaskRuns.CountAsync());
        var run = await _repository.GetAsync(TaskKey);
        Assert.AreEqual("Success", run!.LastRunStatus);
        Assert.AreEqual(200, run.LastRunDurationMs);
        Assert.IsNull(run.LastRunError);
    }

    [TestMethod]
    public async Task RecordRunAsync_DifferentTaskKeys_EachGetsItsOwnRow()
    {
        await _repository.RecordRunAsync(TaskKey, DateTime.UtcNow, 100, succeeded: true, error: null);
        await _repository.RecordRunAsync("some_other_task", DateTime.UtcNow, 200, succeeded: true, error: null);

        using var freshContext = OpenNewContext();
        Assert.AreEqual(2, await freshContext.ScheduledTaskRuns.CountAsync());
    }

    [TestMethod]
    public async Task RecordRunAsync_ConcurrentFirstWritesForTheSameKey_EndUpWithExactlyOneRow()
    {
        // Mirrors LibrarySettingsRepository's get-or-create race: two scopes both find no row for
        // the same task key and both try to insert it. Each caller here runs against its own
        // context, as it would across two request scopes (or the worker's own tick racing the
        // manual "Check Now" endpoint, however unlikely the shared RefreshGate makes that).
        var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            using var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
            var repository = new ScheduledTaskRunRepository(context);
            await repository.RecordRunAsync(TaskKey, DateTime.UtcNow, 100 + i, succeeded: i % 2 == 0, error: null);
        }));

        await Task.WhenAll(tasks);

        using var check = OpenNewContext();
        Assert.AreEqual(1, await check.ScheduledTaskRuns.CountAsync(), "The race must not leave two rows behind");
    }
}
