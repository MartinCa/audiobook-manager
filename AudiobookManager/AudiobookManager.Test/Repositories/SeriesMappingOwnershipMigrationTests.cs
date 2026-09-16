using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Verifies the OwnSeriesMappings migration - the one that turns the global regex mapping table
/// into per-series-owned patterns and, deliberately, deletes every existing row.
/// </summary>
[TestClass]
public class SeriesMappingOwnershipMigrationTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesmappingmigrate-{Guid.NewGuid():N}.db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private DatabaseContext CreateContext() =>
        new(new DbContextOptions<DatabaseContext>(), Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath }));

    /// <summary>
    /// Regression guard for the deliberate data loss: the old global rows (regex + free-text
    /// mapped_series target) are deleted rather than being assigned to an owner they were never
    /// associated with. A guessed owner would silently re-route library series values to the
    /// wrong canonical name, which is worse than making whoever relied on the pattern re-enter it.
    /// </summary>
    [TestMethod]
    public async Task OwnSeriesMappings_DeletesTheLegacyGlobalMappingRows()
    {
        // Migrate up to (but not including) OwnSeriesMappings, then plant legacy-format rows
        // exactly as the old schema stored them.
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync("AddPendingSeriesRefresh");
            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO series_mapping (regex, mapped_series, warn_about_part) VALUES " +
                "('^wheel of time.*$', 'The Wheel of Time', 0), " +
                "('^mistborn.*$', 'Mistborn', 1)");
        }

        using (var migrated = CreateContext())
        {
            await migrated.Database.MigrateAsync();

            Assert.AreEqual(0, await migrated.SeriesMappings.CountAsync(),
                "the legacy global mappings must be cleared, not silently re-owned");

            var columns = await migrated.Database.SqlQueryRaw<string>(
                "SELECT name FROM pragma_table_info('series_mapping')").ToListAsync();
            CollectionAssert.Contains(columns, "series_id");
            CollectionAssert.DoesNotContain(columns, "mapped_series");
        }
    }

    /// <summary>
    /// The post-migration shape must actually work as owned patterns: a mapping points at a
    /// required series row and is cascade-deleted with it.
    /// </summary>
    [TestMethod]
    public async Task OwnSeriesMappings_NewShapeIsOwnedBySeriesAndCascadesWithIt()
    {
        using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();

            var series = new Series { Name = "Mistborn" };
            db.Series.Add(series);
            await db.SaveChangesAsync();

            db.SeriesMappings.Add(new SeriesMapping(default, "^mistborn.*$", true, series.Id));
            await db.SaveChangesAsync();

            var repository = new SeriesMappingRepository(db);
            var mappings = await repository.GetBySeriesNameAsync("Mistborn");
            Assert.AreEqual(1, mappings.Count);
            Assert.AreEqual(series.Id, mappings[0].SeriesId);

            // Deleting the owner must cascade to the pattern: a pattern without its series is
            // meaningless (its target, the series name, would be gone with it).
            db.Remove(series);
            await db.SaveChangesAsync();

            Assert.AreEqual(0, await db.SeriesMappings.CountAsync());
        }
    }
}