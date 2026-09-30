using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AudiobookDb = AudiobookManager.Database.Models.Audiobook;
using AudiobookDomain = AudiobookManager.Domain.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class SeriesRelationSyncTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup() =>
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesrel-{Guid.NewGuid():N}.db");

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

    private static AudiobookDb NewBook() =>
        new(default, "Book", null, null, null, 2024, null, null, null, null, null, null, null, null, 60,
            $"/library/{Guid.NewGuid():N}.m4b", "book.m4b", 10)
        {
            Authors = new List<Database.Models.Person>(),
            Narrators = new List<Database.Models.Person>(),
            Genres = new List<Genre>(),
        };

    private static AudiobookDomain Domain(string? series, string? part, params SeriesRelation[]? additional) =>
        new(new List<AudiobookManager.Domain.Person>(), "Book", 2024, new AudiobookFileInfo("/x.m4b", "x.m4b", 1))
        {
            Series = series,
            SeriesPart = part,
            AdditionalSeries = additional?.ToList(),
        };

    [TestMethod]
    public void Normalize_PrimaryFirst_TrimsDropsBlanksAndDedupesCaseInsensitively()
    {
        var result = SeriesRelationSync.Normalize(
            " Reacher ", " 1 ",
            new[]
            {
                new SeriesRelation("  ", "2"),
                new SeriesRelation("Universe", null),
                new SeriesRelation("reacher", "9"),
                new SeriesRelation("Universe", "4"),
            });

        CollectionAssert.AreEqual(
            new[]
            {
                new SeriesRelationSync.Desired("Reacher", "1", true, 0),
                new SeriesRelationSync.Desired("Universe", "4", false, 1),
            },
            result);
    }

    [TestMethod]
    public void Apply_MirrorsPrimaryOntoBookColumns()
    {
        var book = NewBook();

        SeriesRelationSync.Apply(book, Domain("Reacher", "1", new SeriesRelation("Universe", "3")));

        Assert.AreEqual("Reacher", book.Series);
        Assert.AreEqual("1", book.SeriesPart);
        CollectionAssert.AreEqual(
            new[] { ("Reacher", "1", true), ("Universe", "3", false) },
            book.SeriesRelations!.OrderBy(r => r.SortOrder).Select(r => (r.SeriesName, r.SeriesPart, r.IsPrimary)).ToArray());
    }

    [TestMethod]
    public void Apply_NoPrimary_ClearsMirrorButKeepsAdditional()
    {
        var book = NewBook();

        SeriesRelationSync.Apply(book, Domain(null, null, new SeriesRelation("Universe", "3")));

        Assert.IsNull(book.Series);
        Assert.IsNull(book.SeriesPart);
        Assert.AreEqual(1, book.SeriesRelations!.Count);
        Assert.IsFalse(book.SeriesRelations[0].IsPrimary);
    }

    [TestMethod]
    public void Apply_NullAdditionalSeries_KeepsStoredNonPrimaryRelations()
    {
        var book = NewBook();
        SeriesRelationSync.Apply(book, Domain("Reacher", "1", new SeriesRelation("Universe", "3")));

        // A domain object that never read the relations (AdditionalSeries == null) must not wipe them.
        var unspecified = Domain("Reacher", "2");
        unspecified.AdditionalSeries = null;
        SeriesRelationSync.Apply(book, unspecified);

        Assert.AreEqual("2", book.SeriesPart);
        CollectionAssert.AreEqual(
            new[] { "Reacher", "Universe" },
            book.SeriesRelations!.OrderBy(r => r.SortOrder).Select(r => r.SeriesName).ToArray());
    }

    [TestMethod]
    public void Apply_EmptyAdditionalSeries_RemovesStoredNonPrimaryRelations()
    {
        var book = NewBook();
        SeriesRelationSync.Apply(book, Domain("Reacher", "1", new SeriesRelation("Universe", "3")));

        SeriesRelationSync.Apply(book, Domain("Reacher", "1", Array.Empty<SeriesRelation>()));

        CollectionAssert.AreEqual(new[] { "Reacher" }, book.SeriesRelations!.Select(r => r.SeriesName).ToArray());
    }

    [TestMethod]
    public void Apply_ReturnsEverySeriesNameBeforeAndAfter()
    {
        var book = NewBook();
        SeriesRelationSync.Apply(book, Domain("Old", "1", new SeriesRelation("Shared", "2")));

        var touched = SeriesRelationSync.Apply(book, Domain("New", "1", new SeriesRelation("Shared", "2")));

        CollectionAssert.AreEquivalent(new[] { "Old", "New", "Shared" }, touched.ToArray());
    }

    [TestMethod]
    public async Task Persist_PromotingASecondaryToPrimary_SavesWithoutViolatingTheOnePrimaryIndex()
    {
        await using (var seed = CreateContext())
        {
            await seed.Database.EnsureCreatedAsync();
            var book = NewBook();
            SeriesRelationSync.Apply(book, Domain("A", "1", new SeriesRelation("B", "2")));
            seed.Audiobooks.Add(book);
            await seed.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var book = await db.Audiobooks.Include(a => a.SeriesRelations).SingleAsync();
            // Swap: B becomes primary, A drops to secondary, in one save.
            SeriesRelationSync.Apply(book, Domain("B", "2", new SeriesRelation("A", "1")));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var book = await db.Audiobooks.AsNoTracking().Include(a => a.SeriesRelations).SingleAsync();
            Assert.AreEqual("B", book.Series);
            CollectionAssert.AreEqual(
                new[] { ("B", true), ("A", false) },
                book.SeriesRelations!.OrderBy(r => r.SortOrder).Select(r => (r.SeriesName, r.IsPrimary)).ToArray());
        }
    }

    [TestMethod]
    public async Task Persist_DeletingTheBook_CascadesItsRelations()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        var book = NewBook();
        SeriesRelationSync.Apply(book, Domain("A", "1", new SeriesRelation("B", "2")));
        db.Audiobooks.Add(book);
        await db.SaveChangesAsync();

        db.Audiobooks.Remove(book);
        await db.SaveChangesAsync();

        Assert.AreEqual(0, await db.AudiobookSeries.CountAsync());
    }

    [TestMethod]
    public async Task Persist_FoldsTheSeriesNameForAccentInsensitiveSearch()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        var book = NewBook();
        SeriesRelationSync.Apply(book, Domain("Séries Ünique", null));
        db.Audiobooks.Add(book);
        await db.SaveChangesAsync();

        Assert.AreEqual("Series Unique", (await db.AudiobookSeries.SingleAsync()).SeriesNameFolded);
    }

    [TestMethod]
    public async Task Migration_BackfillsOnePrimaryRelationPerBookWithASeries()
    {
        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync("AddBookQualifiers");
            await seed.Database.ExecuteSqlRawAsync("""
                INSERT INTO audiobooks (book_name, series, series_part, year, qualifiers, file_info_full_path, file_info_file_name, file_info_size_in_bytes)
                VALUES ('One', ' Reacher ', ' 2 ', 2020, '', '/a.m4b', 'a.m4b', 1),
                       ('Two', NULL, NULL, 2020, '', '/b.m4b', 'b.m4b', 1),
                       ('Three', '   ', NULL, 2020, '', '/c.m4b', 'c.m4b', 1),
                       ('Four', 'Séries', '', 2020, '', '/d.m4b', 'd.m4b', 1);
                """);
        }

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            var rows = await db.AudiobookSeries.AsNoTracking().OrderBy(r => r.AudiobookId).ToListAsync();

            CollectionAssert.AreEqual(
                new[] { ("Reacher", "2", true, "Reacher"), ("Séries", null, true, "Series") },
                rows.Select(r => (r.SeriesName, r.SeriesPart, r.IsPrimary, r.SeriesNameFolded)).ToArray());
        }
    }
}
