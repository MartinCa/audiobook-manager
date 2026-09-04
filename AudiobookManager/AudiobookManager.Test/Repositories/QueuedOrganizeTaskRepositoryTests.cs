using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises basic CRUD for the organize queue against a real (temp-file) SQLite database.
/// </summary>
[TestClass]
public class QueuedOrganizeTaskRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private QueuedOrganizeTaskRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"queuedorganizetaskrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new QueuedOrganizeTaskRepository(_db);
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

    private static QueuedOrganizeTask MakeTask(string path, DateTime queuedTime = default) =>
        new QueuedOrganizeTask(path, "{\"bookName\":\"A Book\"}", queuedTime);

    // Regression: DeleteQueuedOrganizeTask uses ExecuteDeleteAsync, which does not go through the
    // change tracker. Both of these passed against the database and failed against the tracker.
    [TestMethod]
    public async Task GetQueuedOrganizeTask_AfterDelete_DoesNotReturnTheDeletedTask()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/gone.m4b"));
        await _repository.DeleteQueuedOrganizeTask("/import/gone.m4b");

        Assert.IsNull(await _repository.GetQueuedOrganizeTask("/import/gone.m4b"));
    }

    [TestMethod]
    public async Task InsertQueuedOrganizeTask_ForAPathThatWasDeletedInThisContext_Succeeds()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/again.m4b"));
        await _repository.DeleteQueuedOrganizeTask("/import/again.m4b");

        var reinserted = await _repository.InsertQueuedOrganizeTask(MakeTask("/import/again.m4b"));

        Assert.AreEqual("/import/again.m4b", reinserted.OriginalFileLocation);
    }

    [TestMethod]
    public async Task InsertQueuedOrganizeTask_PersistsAndIsRetrievable()
    {
        var inserted = await _repository.InsertQueuedOrganizeTask(MakeTask("/import/book.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.AreEqual("/import/book.m4b", inserted.OriginalFileLocation);

        var fetched = await _repository.GetQueuedOrganizeTask("/import/book.m4b");
        Assert.IsNotNull(fetched);
        Assert.AreEqual("{\"bookName\":\"A Book\"}", fetched!.JsonAudiobook);
    }

    [TestMethod]
    public async Task InsertQueuedOrganizeTask_NoQueuedTimeSupplied_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;

        var inserted = await _repository.InsertQueuedOrganizeTask(MakeTask("/import/book.m4b"));

        var after = DateTime.UtcNow;
        Assert.IsTrue(inserted.QueuedTime >= before && inserted.QueuedTime <= after,
            $"expected QueuedTime to default to now; before={before}, actual={inserted.QueuedTime}, after={after}");
    }

    [TestMethod]
    public async Task GetQueuedOrganizeTask_NotFound_ReturnsNull()
    {
        var result = await _repository.GetQueuedOrganizeTask("/nonexistent.m4b");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetAllQueuedOrganizeTasks_ReturnsEveryInsertedTask()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/one.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/two.m4b", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        var all = await _repository.GetAllQueuedOrganizeTasks();

        Assert.AreEqual(2, all.Count);
        CollectionAssert.AreEquivalent(
            new List<string> { "/import/one.m4b", "/import/two.m4b" },
            all.Select(t => t.OriginalFileLocation).ToList());
    }

    [TestMethod]
    public async Task GetAllQueuedOrganizeTasks_EmptyQueue_ReturnsEmptyList()
    {
        var all = await _repository.GetAllQueuedOrganizeTasks();

        Assert.AreEqual(0, all.Count);
    }

    [TestMethod]
    public async Task GetNextQueuedOrganizeTask_ReturnsOldestByQueuedTime()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/newer.m4b", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/older.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var next = await _repository.GetNextQueuedOrganizeTask();

        Assert.IsNotNull(next);
        Assert.AreEqual("/import/older.m4b", next!.OriginalFileLocation);
    }

    [TestMethod]
    public async Task GetNextQueuedOrganizeTask_EmptyQueue_ReturnsNull()
    {
        var next = await _repository.GetNextQueuedOrganizeTask();

        Assert.IsNull(next);
    }

    // DeleteQueuedOrganizeTask uses ExecuteDeleteAsync, a bulk operation that writes straight to
    // the database and bypasses the DbContext's change tracker, while GetQueuedOrganizeTask uses
    // FindAsync, which checks the tracker's local cache before hitting the database. Verifying
    // the delete through the *same* repository/context instance would actually be checking
    // FindAsync's local-cache behavior, not the database - so this reads back through a fresh
    // context/repository, matching how a new scoped DbContext is handed out per web request in
    // production.
    [TestMethod]
    public async Task DeleteQueuedOrganizeTask_RemovesTheMatchingEntry()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/keep.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/remove.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await _repository.DeleteQueuedOrganizeTask("/import/remove.m4b");

        using var freshContext = OpenNewContext();
        var freshRepository = new QueuedOrganizeTaskRepository(freshContext);

        Assert.IsNull(await freshRepository.GetQueuedOrganizeTask("/import/remove.m4b"));
        Assert.IsNotNull(await freshRepository.GetQueuedOrganizeTask("/import/keep.m4b"));
    }

    [TestMethod]
    public async Task DeleteQueuedOrganizeTask_NonexistentPath_DoesNotThrow()
    {
        await _repository.DeleteQueuedOrganizeTask("/nonexistent.m4b");
    }

    [TestMethod]
    public async Task RecordDeserializationFailureAsync_IncrementsCountAndRecordsReasonAndTimestamp()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/bad.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await _repository.RecordDeserializationFailureAsync("/import/bad.m4b", "first failure");
        await _repository.RecordDeserializationFailureAsync("/import/bad.m4b", "second failure");

        var task = await _repository.GetQueuedOrganizeTask("/import/bad.m4b");
        Assert.IsNotNull(task);
        Assert.AreEqual(2, task!.FailureCount);
        Assert.AreEqual("second failure", task.LastFailureReason);
        Assert.IsNotNull(task.LastFailureAt);
    }

    // The whole point of tracking failures: a row that keeps failing must stop coming back so a
    // good row queued behind it can be reached (see #1322).
    [TestMethod]
    public async Task GetNextQueuedOrganizeTask_RowAtFailureThreshold_IsExcludedInFavorOfTheNextOldest()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/bad.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/good.m4b", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));

        for (var i = 0; i < 5; i++)
        {
            await _repository.RecordDeserializationFailureAsync("/import/bad.m4b", $"failure {i}");
        }

        var next = await _repository.GetNextQueuedOrganizeTask();

        Assert.IsNotNull(next);
        Assert.AreEqual("/import/good.m4b", next!.OriginalFileLocation);
    }

    [TestMethod]
    public async Task GetNextQueuedOrganizeTask_RowBelowFailureThreshold_IsStillReturned()
    {
        await _repository.InsertQueuedOrganizeTask(MakeTask("/import/flaky.m4b", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await _repository.RecordDeserializationFailureAsync("/import/flaky.m4b", "transient");

        var next = await _repository.GetNextQueuedOrganizeTask();

        Assert.IsNotNull(next);
        Assert.AreEqual("/import/flaky.m4b", next!.OriginalFileLocation);
    }
}
