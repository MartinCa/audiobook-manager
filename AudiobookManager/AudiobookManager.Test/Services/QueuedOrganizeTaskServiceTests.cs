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
}
