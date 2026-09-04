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
        _db = CreateContext(_dbPath);
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

    private static DatabaseContext CreateContext(string dbLocation) => new(
        new DbContextOptions<DatabaseContext>(),
        Options.Create(new AudiobookManagerSettings { DbLocation = dbLocation }));

    /// <summary>
    /// Opens through EF rather than calling <c>Open()</c> on the raw connection: interceptors are
    /// an EF concern, and a raw open skips them. Reading a pragma back off a raw open would pass
    /// on connection pooling alone - the previous connection's per-connection state coming back
    /// with it - and would not test that the interceptor runs at all.
    /// </summary>
    private static string ScalarPragma(DatabaseContext db, string pragma)
    {
        db.Database.OpenConnection();

        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private string ScalarPragma(string pragma) => ScalarPragma(_db, pragma);

    // Not something this interceptor does - EF Core creates the database in WAL - but the
    // precondition `synchronous = NORMAL` is only safe under, and nothing else in the codebase
    // pins it. If a future EF version stops doing this, NORMAL silently becomes a corruption
    // trade instead of a durability one, and this is the test that says so.
    [TestMethod]
    public void JournalMode_IsWal_WhichIsWhatMakesNormalSafe()
    {
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
        _db.Database.OpenConnection();
        _db.Database.CloseConnection();

        Assert.AreEqual("1", ScalarPragma("synchronous"));
        Assert.AreEqual("30000", ScalarPragma("busy_timeout"));
    }

    // The safety property, not a nicety. synchronous = NORMAL costs only durability under WAL,
    // but under any other journal mode SQLite documents it as risking corruption on power loss.
    // Not every database file reaches this application in WAL: EF creates one that way, but a
    // file restored from a backup tool or made with the sqlite3 CLI arrives in `delete` mode and
    // EF leaves an existing file's mode alone. An in-memory database stands in for that here -
    // its journal mode is 'memory' and is not WAL - and the assertion is that NORMAL is withheld.
    [TestMethod]
    public void WhenTheDatabaseIsNotInWal_SynchronousIsLeftAtItsSafeDefault()
    {
        using var db = CreateContext(":memory:");
        db.Database.EnsureCreated();

        var mode = ScalarPragma(db, "journal_mode");
        Assert.IsFalse(
            string.Equals("wal", mode, StringComparison.OrdinalIgnoreCase),
            "An in-memory database was expected not to be in WAL; this test no longer covers the non-WAL path.");
        Assert.AreEqual("2", ScalarPragma(db, "synchronous"), $"journal_mode was '{mode}', so synchronous must stay FULL.");

        // Still applied: unconditionally safe, and unrelated to durability.
        Assert.AreEqual("30000", ScalarPragma(db, "busy_timeout"));
    }

    [TestMethod]
    public void AccentFoldingStillWorks_AlongsideThePragmas()
    {
        // Both interceptors hook connection open; registering the second must not displace the
        // first, whose scalar function every search predicate depends on.
        _db.Database.OpenConnection();

        using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT fold_accents('René');";

        Assert.AreEqual("Rene", command.ExecuteScalar()?.ToString());
    }
}
