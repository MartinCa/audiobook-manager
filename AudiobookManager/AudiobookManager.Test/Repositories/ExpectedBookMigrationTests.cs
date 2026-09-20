using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Verifies the two-roster-to-unified migration pair. <c>AddUnifiedExpectedBooks</c> copies
/// <c>series_expected_books</c> and <c>author_expected_books</c> rows into the new
/// <c>expected_books</c>/<c>expected_book_authors</c> tables with a synthetic source_book_id;
/// <c>DropLegacyExpectedBookTables</c> then drops the legacy tables (and their indexes) now that
/// the copy has landed. The legacy entities no longer exist, so the legacy rows are planted with
/// raw SQL against the pre-drop schema.
/// </summary>
[TestClass]
public class ExpectedBookMigrationTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"expectedbookmigrate-{Guid.NewGuid():N}.db");
    }

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

    /// <summary>
    /// Seeds the pre-AddUnifiedExpectedBooks schema with legacy-format roster rows. The legacy
    /// row ids are deterministic (the first inserted row of each table is id 1, the second id 2),
    /// which is what the synthetic source_book_id assertions below rely on.
    /// </summary>
    private async Task<SeedData> SeedLegacyRostersAsync(DatabaseContext seed)
    {
        await seed.Database.MigrateAsync("AddAudiobookMatchedSourceIndex");

        var series = new Series
        {
            Name = "The Stormlight Archive",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "hc-series-1",
            MatchedSeriesName = "The Stormlight Archive",
        };
        seed.Series.Add(series);

        var matched = new Person(default, "Brandon Sanderson")
        {
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "hc-author-1",
        };
        var unmatched = new Person(default, "Robin Hobb")
        {
            MatchedSourceName = null,
        };
        seed.Persons.AddRange(matched, unmatched);
        await seed.SaveChangesAsync();

        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO series_expected_books
                (series_id, position, title, year, release_date, source_url, is_ignored, is_compilation)
            VALUES
                ({series.Id}, '1', 'The Way of Kings', 2010, '2010-08-31',
                 'https://hardcover.app/books/the-way-of-kings', 1, 0);
            """);

        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO author_expected_books
                (person_id, title, year, release_date, source_url, is_ignored)
            VALUES
                ({matched.Id}, 'Elantris', 2005, NULL, NULL, 0);
            """);
        // The second author has no matched source: the copy must fall back to 'Hardcover'.
        await seed.Database.ExecuteSqlAsync($"""
            INSERT INTO author_expected_books
                (person_id, title, year, release_date, source_url, is_ignored)
            VALUES
                ({unmatched.Id}, 'Assassin''s Apprentice', 1995, '1995-04-01',
                 'https://hardcover.app/books/assassins-apprentice', 1);
            """);

        return new SeedData(matched.Id, unmatched.Id);
    }

    private sealed record SeedData(long MatchedPersonId, long UnmatchedPersonId);

    /// <summary>
    /// Plants legacy-format rows in the pre-AddUnifiedExpectedBooks schema, runs the full
    /// migration chain (copy, then legacy-table drop) and checks every copied field -
    /// series/author links and is_ignored included - plus the legacy tables' disappearance.
    /// </summary>
    [TestMethod]
    public async Task AddUnifiedExpectedBooks_CopiesBothLegacyRostersWithSyntheticSourceBookIds()
    {
        SeedData seedData;
        using (var seed = CreateContext())
        {
            seedData = await SeedLegacyRostersAsync(seed);
        }

        using (var migrated = CreateContext())
        {
            await migrated.Database.MigrateAsync();

            var seriesBooks = await migrated.ExpectedBooks
                .Include(b => b.AuthorLinks)
                .ToListAsync();
            Assert.AreEqual(3, seriesBooks.Count);

            var copiedSeries = seriesBooks.Single(b =>
                b.SourceBookId!.StartsWith(ExpectedBook.LegacySeriesSyntheticPrefix, StringComparison.Ordinal));
            Assert.AreEqual("Hardcover", copiedSeries.SourceName);
            Assert.AreEqual("The Way of Kings", copiedSeries.Title);
            Assert.AreEqual(2010, copiedSeries.Year);
            Assert.AreEqual(new DateOnly(2010, 8, 31), copiedSeries.ReleaseDate);
            Assert.AreEqual("https://hardcover.app/books/the-way-of-kings", copiedSeries.SourceUrl);
            Assert.AreEqual("1", copiedSeries.SeriesPosition);
            Assert.AreEqual("hc-series-1", copiedSeries.SourceSeriesId);
            Assert.AreEqual("The Stormlight Archive", copiedSeries.SourceSeriesName);
            Assert.IsTrue(copiedSeries.SeriesId.HasValue);
            Assert.IsFalse(copiedSeries.IsCompilation);
            Assert.IsTrue(copiedSeries.IsIgnored, "is_ignored must survive the copy");
            Assert.AreEqual(0, copiedSeries.AuthorLinks.Count,
                "a series-migrated row has no author links - it is adopted by a refresh later");

            var matchedAuthorBook = seriesBooks.Single(b => b.SourceBookId == ExpectedBook.LegacyAuthorSyntheticPrefix + "1");
            Assert.AreEqual("Hardcover", matchedAuthorBook.SourceName);
            Assert.AreEqual("Elantris", matchedAuthorBook.Title);
            Assert.AreEqual(2005, matchedAuthorBook.Year);
            Assert.IsNull(matchedAuthorBook.SourceUrl);
            Assert.IsFalse(matchedAuthorBook.IsCompilation);
            Assert.IsFalse(matchedAuthorBook.IsIgnored);

            var fallbackAuthorBook = seriesBooks.Single(b => b.SourceBookId == ExpectedBook.LegacyAuthorSyntheticPrefix + "2");
            Assert.AreEqual("Hardcover", fallbackAuthorBook.SourceName,
                "an unmatched author must fall back to the default source name 'Hardcover'");
            Assert.AreEqual("Assassin's Apprentice", fallbackAuthorBook.Title);
            Assert.AreEqual(new DateOnly(1995, 4, 1), fallbackAuthorBook.ReleaseDate);
            Assert.AreEqual("https://hardcover.app/books/assassins-apprentice", fallbackAuthorBook.SourceUrl);
            Assert.IsTrue(fallbackAuthorBook.IsIgnored);

            var links = await migrated.ExpectedBookAuthors.AsNoTracking().ToListAsync();
            Assert.AreEqual(2, links.Count);
            CollectionAssert.AreEquivalent(
                new[] { seedData.MatchedPersonId, seedData.UnmatchedPersonId },
                links.Select(l => l.PersonId).ToList(),
                "each migrated author roster row must carry its person link");
            CollectionAssert.AreEquivalent(
                new[] { "Brandon Sanderson", "Robin Hobb" },
                links.Select(l => l.AuthorName).ToList());

            // The author-link person FK is SET NULL (a deleted Person must not cascade the link
            // away - the stored AuthorName is the roster's fallback identity), and the book's
            // series FK is SET NULL too (a deleted series must not cascade the book).
            var linkOnDelete = await migrated.Database.SqlQuery<string>($"""
                SELECT "on_delete" FROM pragma_foreign_key_list('expected_book_authors')
                WHERE "from" = 'person_id'
                """).ToListAsync();
            CollectionAssert.AreEquivalent(new[] { "SET NULL" }, linkOnDelete,
                "expected_book_authors.person_id must be ON DELETE SET NULL");
            var bookOnDelete = await migrated.Database.SqlQuery<string>($"""
                SELECT "on_delete" FROM pragma_foreign_key_list('expected_books')
                WHERE "from" = 'series_id'
                """).ToListAsync();
            CollectionAssert.AreEquivalent(new[] { "SET NULL" }, bookOnDelete,
                "expected_books.series_id must be ON DELETE SET NULL");

            // The final migration in the chain drops the legacy tables now that their rows are
            // copied - they must be gone, names and indexes alike.
            Assert.IsFalse(await TableExists("series_expected_books"));
            Assert.IsFalse(await TableExists("author_expected_books"));
            Assert.AreEqual(0, (await IndexNamesAsync("ix_series_expected_books_series_id")).Count);
            Assert.AreEqual(0, (await IndexNamesAsync("ix_author_expected_books_person_id")).Count);
        }
    }

    /// <summary>
    /// AddUnifiedExpectedBooks' Down() must drop only the two new tables - the legacy roster
    /// tables are untouched by that migration, so a rollback of just it loses only the copied
    /// rows and the app keeps working against the old tables. (The later
    /// DropLegacyExpectedBookTables migration is not applied when targeting AddUnifiedExpectedBooks.)
    /// </summary>
    [TestMethod]
    public async Task AddUnifiedExpectedBooks_DownDropsOnlyTheNewTables()
    {
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync("AddUnifiedExpectedBooks");
            Assert.IsTrue(await TableExists("expected_books"));
            Assert.IsTrue(await TableExists("expected_book_authors"));
        }

        using (var rolledBack = CreateContext())
        {
            await rolledBack.Database.MigrateAsync("AddAudiobookMatchedSourceIndex");

            Assert.IsFalse(await TableExists("expected_books"),
                "rolling back must drop the new table");
            Assert.IsFalse(await TableExists("expected_book_authors"),
                "rolling back must drop the new author-link table");
            // The legacy tables survive rolling back AddUnifiedExpectedBooks - they are touched
            // only by the later DropLegacyExpectedBookTables migration, not by this one.
            Assert.IsTrue(await TableExists("series_expected_books"));
            Assert.IsTrue(await TableExists("author_expected_books"));
        }
    }

    /// <summary>
    /// DropLegacyExpectedBookTables' Down() recreates the legacy tables schema-only - SQLite
    /// cannot undo a table drop's data loss, and the copied rows live on in expected_books
    /// anyway - so a downgraded build has working (empty) tables to read rather than none.
    /// </summary>
    [TestMethod]
    public async Task DropLegacyExpectedBookTables_DownRecreatesSchemaOnly_DataIsNotRestored()
    {
        using (var seed = CreateContext())
        {
            await SeedLegacyRostersAsync(seed);
        }

        using (var migrated = CreateContext())
        {
            await migrated.Database.MigrateAsync();
            Assert.IsFalse(await TableExists("series_expected_books"));
            Assert.IsFalse(await TableExists("author_expected_books"));
        }

        using (var rolledBack = CreateContext())
        {
            await rolledBack.Database.MigrateAsync("AddUnifiedExpectedBooks");

            // Both legacy tables come back with their schema (columns, pk, FK and the index),
            // but empty - the rows are not restored by a SQLite drop.
            Assert.IsTrue(await TableExists("series_expected_books"));
            Assert.IsTrue(await TableExists("author_expected_books"));
            Assert.AreEqual(1, (await IndexNamesAsync("ix_series_expected_books_series_id")).Count);
            Assert.AreEqual(1, (await IndexNamesAsync("ix_author_expected_books_person_id")).Count);

            var seriesRows = await rolledBack.Database.SqlQuery<long>(
                $"SELECT COUNT(*) FROM series_expected_books").ToListAsync();
            Assert.AreEqual(0, seriesRows.Single());
            var authorRows = await rolledBack.Database.SqlQuery<long>(
                $"SELECT COUNT(*) FROM author_expected_books").ToListAsync();
            Assert.AreEqual(0, authorRows.Single());

            // The rollback target is right after the copy migration, so the copied rows still
            // live in the new tables - which is exactly what the schema-only legacy tables are
            // for: a downgraded build reads an empty legacy roster instead of the copied data.
            Assert.IsTrue(await TableExists("expected_books"));
            var copiedRows = await rolledBack.Database.SqlQuery<long>(
                $"SELECT COUNT(*) FROM expected_books").ToListAsync();
            Assert.AreEqual(3, copiedRows.Single());
        }
    }

    private async Task<bool> TableExists(string tableName)
    {
        using (var db = CreateContext())
        {
            var names = await db.Database.SqlQuery<string>(
                $"SELECT name FROM sqlite_master WHERE type = 'table' AND name = {tableName}").ToListAsync();
            return names.Count > 0;
        }
    }

    /// <summary>
    /// Returns the names of every index matching <paramref name="indexName"/> - empty once the
    /// legacy tables (and the indexes bound to them) are dropped. The parameter is interpolated
    /// into the query; SQLite databases are per-test temp files, so there is no injection surface
    /// worth escaping here.
    /// </summary>
    private async Task<List<string>> IndexNamesAsync(string indexName)
    {
        using (var db = CreateContext())
        {
            return await db.Database.SqlQuery<string>(
                $"SELECT name FROM sqlite_master WHERE type = 'index' AND name = {indexName}").ToListAsync();
        }
    }
}