using AudiobookManager.Database;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Services;

/// <summary>
/// Runs against a real (temp-file) SQLite database rather than a mocked repository, because the
/// behaviour under test is what the primary key does - a mock would only assert the arrangement.
/// </summary>
[TestClass]
public class QueuedOrganizeTaskServiceTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private QueuedOrganizeTaskService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"queueservice-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _service = new QueuedOrganizeTaskService(
            new QueuedOrganizeTaskRepository(_db),
            new Mock<ILogger<QueuedOrganizeTaskService>>().Object);
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

    private static Audiobook MakeBook(string path) =>
        new(new List<Person>(), "A Book", 2024, new AudiobookFileInfo(path, Path.GetFileName(path), 1000));

    [TestMethod]
    public async Task QueueOrganizeTask_QueuesTheFile()
    {
        var task = await _service.QueueOrganizeTask(MakeBook("/import/a.m4b"));

        Assert.AreEqual("/import/a.m4b", task.OriginalFileLocation);
        Assert.AreEqual(1, (await _service.GetQueuedOrganizeTasks()).Count);
    }

    // The user clicking Organize twice. Before, the second call reached the insert and threw
    // DbUpdateException on the primary key, which surfaced as an empty 500.
    [TestMethod]
    public async Task QueueOrganizeTask_SameFileTwice_ThrowsAlreadyQueued()
    {
        await _service.QueueOrganizeTask(MakeBook("/import/a.m4b"));

        var ex = await Assert.ThrowsExactlyAsync<OrganizeTaskAlreadyQueuedException>(
            () => _service.QueueOrganizeTask(MakeBook("/import/a.m4b")));

        Assert.AreEqual("/import/a.m4b", ex.OriginalFileLocation);
        Assert.AreEqual(1, (await _service.GetQueuedOrganizeTasks()).Count, "The second queue must not have written a row.");
    }

    // Different files are unrelated - the guard keys on the path, not on "something is queued".
    [TestMethod]
    public async Task QueueOrganizeTask_DifferentFiles_BothQueue()
    {
        await _service.QueueOrganizeTask(MakeBook("/import/a.m4b"));
        await _service.QueueOrganizeTask(MakeBook("/import/b.m4b"));

        Assert.AreEqual(2, (await _service.GetQueuedOrganizeTasks()).Count);
    }

    // Re-queuing after the worker has taken the file is the normal way a retry happens, so the
    // conflict must not outlive the row it is about.
    [TestMethod]
    public async Task QueueOrganizeTask_AfterTheEarlierTaskIsRemoved_QueuesAgain()
    {
        await _service.QueueOrganizeTask(MakeBook("/import/a.m4b"));
        await _service.DeleteQueuedOrganizeTask("/import/a.m4b");

        var task = await _service.QueueOrganizeTask(MakeBook("/import/a.m4b"));

        Assert.AreEqual("/import/a.m4b", task.OriginalFileLocation);
    }

    // Regression for #1322: a row whose json_audiobook cannot be deserialised (a corrupt value, or
    // a breaking change to the Audiobook shape landing after the row was queued) must not be
    // returned as a task, and must not silently vanish - the caller needs to know which row failed
    // so it can be reported, and the row itself has to survive so a later fix can still read it.
    [TestMethod]
    public async Task GetNextQueuedOrganizeTask_UndeserialisableRow_ThrowsWithTheRowsPathAndLeavesItInPlace()
    {
        await InsertRawAsync("/import/corrupt.m4b", "not valid json", DateTime.UtcNow);

        var ex = await Assert.ThrowsExactlyAsync<QueuedOrganizeTaskDeserializationException>(
            () => _service.GetNextQueuedOrganizeTask());

        Assert.AreEqual("/import/corrupt.m4b", ex.OriginalFileLocation);

        // Read the raw row rather than through the service: GetQueuedOrganizeTask deserializes
        // too, and would throw the same way. What matters here is that the row still exists.
        var raw = await new QueuedOrganizeTaskRepository(_db).GetQueuedOrganizeTask("/import/corrupt.m4b");
        Assert.IsNotNull(raw,
            "the row must not be deleted - its JSON may be the only surviving copy of the user's edits");
        Assert.AreEqual(1, raw!.FailureCount);
    }

    // The actual bug in #1322: nothing stopped the same unreadable row from being picked again on
    // every call, forever, which meant a good row queued behind it could never run. After enough
    // consecutive failures the bad row must be skipped so the queue can move on.
    [TestMethod]
    public async Task GetNextQueuedOrganizeTask_RowFailsRepeatedly_EventuallyStopsBlockingLaterGoodRows()
    {
        var queuedAt = DateTime.UtcNow;
        await InsertRawAsync("/import/corrupt.m4b", "not valid json", queuedAt);
        await _service.QueueOrganizeTask(MakeBook("/import/good.m4b"));
        // Give the good row a later QueuedTime so it would never be picked ahead of the bad one on
        // its own - only the bad row dropping out of contention can surface it.
        await SetQueuedTimeAsync("/import/good.m4b", queuedAt.AddMinutes(1));

        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var task = await _service.GetNextQueuedOrganizeTask();
                Assert.AreEqual("/import/good.m4b", task?.OriginalFileLocation,
                    $"expected the good row once the bad one dead-letters (attempt {attempt})");
                return;
            }
            catch (QueuedOrganizeTaskDeserializationException)
            {
                // Expected for the first several attempts, mirroring the worker retrying the same
                // read on its next loop iteration.
            }
        }

        Assert.Fail($"The bad row was still being returned after {maxAttempts} attempts; it never dead-lettered.");
    }

    private async Task InsertRawAsync(string originalFileLocation, string jsonAudiobook, DateTime queuedTime)
    {
        _db.QueuedOrganizeTasks.Add(new Database.Models.QueuedOrganizeTask(originalFileLocation, jsonAudiobook, queuedTime));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task SetQueuedTimeAsync(string originalFileLocation, DateTime queuedTime)
    {
        await _db.QueuedOrganizeTasks
            .Where(x => x.OriginalFileLocation == originalFileLocation)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.QueuedTime, queuedTime));
    }
}
