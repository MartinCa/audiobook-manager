using AudiobookManager.Database;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DbInitialsSpacing = AudiobookManager.Database.Models.InitialsSpacing;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the single-row library settings table against a real (temp-file) SQLite database:
/// the interesting behavior is the bootstrap-on-first-read and the get-or-create race handling,
/// both of which need an actual database to mean anything.
/// </summary>
[TestClass]
public class LibrarySettingsRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private LibrarySettingsRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"librarysettingsrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new LibrarySettingsRepository(_db);
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
    public async Task GetOrCreateAsync_NoRowYet_CreatesRowWithUnspacedDefault()
    {
        var settings = await _repository.GetOrCreateAsync();

        Assert.AreEqual(DbInitialsSpacing.Unspaced, settings.InitialsSpacing);

        // The row must actually persist, not just be returned - a second context sees it.
        using var freshContext = OpenNewContext();
        Assert.AreEqual(1, await freshContext.LibrarySettings.CountAsync());
    }

    [TestMethod]
    public async Task GetOrCreateAsync_RowExists_ReturnsItWithoutInserting()
    {
        await _repository.UpdateAsync(DbInitialsSpacing.Spaced);

        var settings = await _repository.GetOrCreateAsync();

        Assert.AreEqual(DbInitialsSpacing.Spaced, settings.InitialsSpacing);
        Assert.AreEqual(1, await _db.LibrarySettings.CountAsync());
    }

    [TestMethod]
    public async Task UpdateAsync_NoRowYet_CreatesRowWithTheRequestedValue()
    {
        var settings = await _repository.UpdateAsync(DbInitialsSpacing.Spaced);

        Assert.AreEqual(DbInitialsSpacing.Spaced, settings.InitialsSpacing);
        using var freshContext = OpenNewContext();
        Assert.AreEqual(DbInitialsSpacing.Spaced, (await freshContext.LibrarySettings.SingleAsync()).InitialsSpacing);
    }

    [TestMethod]
    public async Task UpdateAsync_RowExists_UpdatesItInPlace()
    {
        await _repository.UpdateAsync(DbInitialsSpacing.Spaced);
        await _repository.UpdateAsync(DbInitialsSpacing.Unspaced);

        Assert.AreEqual(1, await _db.LibrarySettings.CountAsync());
        Assert.AreEqual(DbInitialsSpacing.Unspaced, (await _repository.GetOrCreateAsync()).InitialsSpacing);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_ConcurrentFirstReads_EndUpWithExactlyOneRow()
    {
        // Mirrors the get-or-create race PersonRepository guards: two scopes both find no row on
        // a live database and both try to insert. Each caller here runs against its own context,
        // as it would across two request scopes.
        var otherDbPath = _dbPath;
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = otherDbPath });
            using var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
            var repository = new LibrarySettingsRepository(context);
            var result = await repository.GetOrCreateAsync();
            return result.InitialsSpacing;
        }));

        var results = await Task.WhenAll(tasks);

        Assert.IsTrue(results.All(r => r == DbInitialsSpacing.Unspaced), "Every caller gets the default");
        using var check = OpenNewContext();
        Assert.AreEqual(1, await check.LibrarySettings.CountAsync(), "The race must not leave two rows behind");
    }

    [TestMethod]
    public async Task UpdateAsync_ConcurrentFirstWrites_EndUpWithExactlyOneRow()
    {
        // The get-or-create rescue in UpdateAsync must cover the race as well: two callers on
        // separate contexts both find no row, both insert the singleton id, and only one wins.
        // The loser must adopt the winner's row and apply its value to it rather than surfacing
        // an unhandled UNIQUE violation - and the database must hold exactly one row with a
        // deterministic end value.
        var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            using var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
            var repository = new LibrarySettingsRepository(context);
            return await repository.UpdateAsync(
                i % 2 == 0 ? DbInitialsSpacing.Spaced : DbInitialsSpacing.Unspaced);
        }));

        await Task.WhenAll(tasks);

        using var check = OpenNewContext();
        Assert.AreEqual(1, await check.LibrarySettings.CountAsync(), "The race must not leave two rows behind");
    }
}