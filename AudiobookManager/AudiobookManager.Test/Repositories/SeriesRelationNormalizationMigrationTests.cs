using AudiobookManager.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The NormalizeSeriesStorage migration puts legacy series values in canonical form, makes
/// audiobook_series agree with the flat columns and then drops them (#1564).
/// </summary>
[TestClass]
public class SeriesRelationNormalizationMigrationTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup() => _dbPath = Path.Combine(Path.GetTempPath(), $"seriesnorm-{Guid.NewGuid():N}.db");

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private DatabaseContext CreateContext() =>
        new(new DbContextOptions<DatabaseContext>(), Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath }));

    private static string Insert(long id, string? series, string? part) =>
        "INSERT INTO audiobooks (id, book_name, year, series, series_part, series_folded, qualifiers, file_info_file_name, file_info_full_path, file_info_size_in_bytes) " +
        $"VALUES ({id}, 'Book {id}', 2000, {Sql(series)}, {Sql(part)}, {Sql(series)}, '', 'f{id}.m4b', '/lib/f{id}.m4b', 1)";

    private static string Sql(string? v) => v is null ? "NULL" : $"'{v.Replace("'", "''")}'";

    private static string Rel(long book, string name, string? part, bool primary, int order) =>
        "INSERT INTO audiobook_series (audiobook_id, series_name, series_part, is_primary, sort_order, series_name_folded) " +
        $"VALUES ({book}, {Sql(name)}, {Sql(part)}, {(primary ? 1 : 0)}, {order}, {Sql(name)})";

    [TestMethod]
    public async Task Migration_RepairsDriftAndCanonicalisesLegacyValues_AndDropsTheFlatColumns()
    {
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync("AddFilterPresets");
            var statements = new[]
            {
                // 1: padded mirror, no relation row at all (the old backfill skipped nothing but later writes could).
                Insert(1, "  Mistborn  ", " 2 "),
                // 2: whitespace-only series: no series.
                Insert(2, "   ", "3"),
                // 3: primary row names another series than the mirror; the mirror (what the file says) wins.
                Insert(3, "Stormlight", "1"),
                Rel(3, "Old Name", "9", true, 0),
                // 4: same additional series under two casings, plus one that duplicates the primary.
                Insert(4, "Main", "1"),
                Rel(4, "Main", "1", true, 0),
                Rel(4, "Spinoff", "2", false, 1),
                Rel(4, "spinoff", null, false, 2),
                Rel(4, "main", "5", false, 3),
                // 5: part drifted between mirror and primary row.
                Insert(5, "Cosmere", "Book 2"),
                Rel(5, "Cosmere", "1", true, 0),
                // 6: no mirror but a primary row: the row's series becomes the mirror.
                Insert(6, null, null),
                Rel(6, "Elantris", "1", true, 0),
                // 7: a book with a series and only an additional row (no primary).
                Insert(7, "Alpha", null),
                Rel(7, "Beta", "1", false, 0),
            };
            foreach (var statement in statements)
            {
                await seed.Database.ExecuteSqlRawAsync(statement);
            }
        }

        using var db = CreateContext();
        await db.Database.MigrateAsync();

        var books = await db.Audiobooks.AsNoTracking().Include(a => a.SeriesRelations).ToDictionaryAsync(a => a.Id);
        (string Name, string? Part, bool Primary)[] Relations(long id) =>
            books[id].SeriesRelations!.OrderBy(r => r.SortOrder).Select(r => (r.SeriesName, r.SeriesPart, r.IsPrimary)).ToArray();

        CollectionAssert.AreEqual(new[] { ("Mistborn", (string?)"2", true) }, Relations(1));

        Assert.AreEqual(0, books[2].SeriesRelations!.Count, "a whitespace-only series is no series");

        CollectionAssert.AreEqual(new[] { ("Stormlight", (string?)"1", true) }, Relations(3));

        CollectionAssert.AreEqual(new[] { ("Main", (string?)"1", true), ("Spinoff", (string?)"2", false) }, Relations(4));

        CollectionAssert.AreEqual(new[] { ("Cosmere", (string?)"Book 2", true) }, Relations(5));

        CollectionAssert.AreEqual(new[] { ("Elantris", (string?)"1", true) }, Relations(6));

        CollectionAssert.AreEqual(new[] { ("Alpha", (string?)null, true), ("Beta", (string?)"1", false) }, Relations(7));

        var columns = await db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('audiobooks')").ToListAsync();
        foreach (var dropped in new[] { "series", "series_part", "series_folded" })
        {
            CollectionAssert.DoesNotContain(columns, dropped, "the flat copy of the primary series is gone");
        }
    }

    [TestMethod]
    public async Task Down_RestoresTheFlatColumnsFromThePrimaryRelation()
    {
        using var db = CreateContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO audiobooks (id, book_name, year, qualifiers, file_info_file_name, file_info_full_path, file_info_size_in_bytes) VALUES (1, 'B', 2000, '', 'f.m4b', '/lib/f.m4b', 1)");
        await db.Database.ExecuteSqlRawAsync(Rel(1, "Main", "2", true, 0));
        await db.Database.ExecuteSqlRawAsync(Rel(1, "Spinoff", "1", false, 1));

        await db.Database.GetService<IMigrator>().MigrateAsync("AddFilterPresets");

        var row = (await db.Database.SqlQueryRaw<string>("SELECT series || '|' || series_part AS \"Value\" FROM audiobooks WHERE id = 1").ToListAsync()).Single();
        Assert.AreEqual("Main|2", row);
    }
}
