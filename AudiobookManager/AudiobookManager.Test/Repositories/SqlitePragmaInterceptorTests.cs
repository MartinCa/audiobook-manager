using AudiobookManager.Database;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Asserts the pragmas are actually in effect on a connection the application built, rather than
/// that the interceptor was registered - the registration is not the property anyone cares about.
/// </summary>
[TestClass]
public class SqlitePragmaInterceptorTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pragmas-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
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

    private string ScalarPragma(string pragma)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public void JournalMode_IsWal()
    {
        // The point of the change: in the default rollback-journal mode a writer blocks every
        // reader for its whole transaction, and this application deliberately runs the organize
        // worker, background scans and interactive saves at the same time.
        var mode = ScalarPragma("journal_mode");

        Assert.IsTrue(
            string.Equals("wal", mode, StringComparison.OrdinalIgnoreCase),
            $"Expected journal_mode 'wal', got '{mode}'.");
    }

    [TestMethod]
    public void Synchronous_IsNormal()
    {
        // 1 == NORMAL. Per-connection, so unlike journal_mode it has to be reapplied on every
        // open - which is why this is an interceptor rather than a one-off at startup.
        Assert.AreEqual("1", ScalarPragma("synchronous"));
    }

    [TestMethod]
    public void BusyTimeout_IsSet()
    {
        Assert.AreEqual("30000", ScalarPragma("busy_timeout"));
    }

    [TestMethod]
    public void PerConnectionPragmas_SurviveAClosedAndReopenedConnection()
    {
        // Pooling hands back a connection that has already been used. The interceptor runs on
        // every open, so a recycled connection must not come back with the defaults.
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }
        connection.Close();
        connection.Open();

        Assert.AreEqual("1", ScalarPragma("synchronous"));
        Assert.AreEqual("30000", ScalarPragma("busy_timeout"));
    }

    [TestMethod]
    public void AccentFoldingStillWorks_AlongsideThePragmas()
    {
        // Both interceptors hook connection open; registering the second must not displace the
        // first, whose scalar function every search predicate depends on.
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT fold_accents('René');";

        Assert.AreEqual("Rene", command.ExecuteScalar()?.ToString());
    }
}
