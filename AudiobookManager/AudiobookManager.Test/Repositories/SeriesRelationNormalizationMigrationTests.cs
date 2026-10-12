using AudiobookManager.Database;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The NormalizeSeriesRelations migration puts legacy series values in canonical form and makes
/// audiobook_series agree with the mirrored columns (#1564), and the drift query is the guard that
/// the two never disagree again.
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
    public async Task Migration_RepairsDriftAndCanonicalisesLegacyValues_AndDriftGuardIsThenEmpty()
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

        Assert.AreEqual("Mistborn", books[1].Series);
        Assert.AreEqual("2", books[1].SeriesPart);
        CollectionAssert.AreEqual(new[] { ("Mistborn", (string?)"2", true) }, Relations(1));

        Assert.IsNull(books[2].Series);
        Assert.IsNull(books[2].SeriesPart);
        Assert.AreEqual(0, books[2].SeriesRelations!.Count);

        CollectionAssert.AreEqual(new[] { ("Stormlight", (string?)"1", true) }, Relations(3));

        CollectionAssert.AreEqual(new[] { ("Main", (string?)"1", true), ("Spinoff", (string?)"2", false) }, Relations(4));

        Assert.AreEqual("Book 2", books[5].SeriesPart, "parts are never rewritten, only the stale row follows the mirror");
        CollectionAssert.AreEqual(new[] { ("Cosmere", (string?)"Book 2", true) }, Relations(5));

        Assert.AreEqual("Elantris", books[6].Series);
        Assert.AreEqual("1", books[6].SeriesPart);

        Assert.AreEqual("Alpha", books[7].Series);
        CollectionAssert.AreEqual(new[] { ("Alpha", (string?)null, true), ("Beta", (string?)"1", false) }, Relations(7));

        Assert.AreEqual(0, (await new AudiobookRepository(db).GetSeriesMirrorDriftIdsAsync(100)).Count,
            "after the migration no book's mirror may disagree with its primary relation");
    }

    [TestMethod]
    public async Task DriftGuard_FlagsAMirrorThatDisagreesWithThePrimaryRelation()
    {
        using var db = CreateContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(Insert(1, "Main", "1"));
        await db.Database.ExecuteSqlRawAsync(Rel(1, "Main", "1", true, 0));
        await db.Database.ExecuteSqlRawAsync(Insert(2, "Main", "2"));
        await db.Database.ExecuteSqlRawAsync(Rel(2, "Main", "1", true, 0));
        await db.Database.ExecuteSqlRawAsync(Insert(3, "Lonely", null));

        CollectionAssert.AreEqual(new long[] { 2, 3 }, await new AudiobookRepository(db).GetSeriesMirrorDriftIdsAsync(100));
        CollectionAssert.AreEqual(new long[] { 2 }, await new AudiobookRepository(db).GetSeriesMirrorDriftIdsAsync(1));
    }
}
