using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The main author column against real SQLite: the migration adds it to an audiobooks table that
/// already holds rows (SQLite rebuilds the table to add the foreign key), and removing the person it
/// points at leaves the book without a main author instead of failing or deleting the book.
/// </summary>
[TestClass]
public class MainAuthorMigrationAndRelationTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"mainauthor-{Guid.NewGuid():N}.db");
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

    [TestMethod]
    public async Task Migration_OnALibraryThatAlreadyHasBooks_KeepsThemAndGivesThemNoMainAuthor()
    {
        await using (var old = CreateContext())
        {
            await old.Database.MigrateAsync("AddAutoSplitTitleOnColon");
            await old.Database.ExecuteSqlAsync($"""
                INSERT INTO persons (name) VALUES ('Existing Author');
                """);
            await old.Database.ExecuteSqlAsync($"""
                INSERT INTO audiobooks (book_name, year, file_info_full_path, file_info_file_name, file_info_size_in_bytes)
                VALUES ('Existing Book', 2020, '/library/Existing Author/2020 - Existing Book/b.m4b', 'b.m4b', 100);
                """);
            await old.Database.ExecuteSqlAsync($"""
                INSERT INTO library_settings (id, initials_spacing, initials_punctuation, metadata_refresh_delay_ms, upcoming_releases_enabled, upcoming_releases_cron_schedule, default_page_size, search_initials_handling, include_narrator_in_path, max_narrators_in_path, auto_split_title_on_colon)
                VALUES (1, 0, 0, 1000, 1, '0 3 * * *', 20, 0, 0, 1, 0);
                """);
        }

        await using var migrated = CreateContext();
        await migrated.Database.MigrateAsync();

        var book = await migrated.Audiobooks.AsNoTracking().SingleAsync();
        Assert.AreEqual("Existing Book", book.BookName);
        Assert.IsNull(book.MainAuthorId);

        var settings = await migrated.LibrarySettings.AsNoTracking().SingleAsync();
        Assert.IsFalse(settings.MainAuthorOnlyInPath, "the new setting must default to off so no existing book moves");
        Assert.IsNull(settings.MetadataSourceSearchJson);
    }

    [TestMethod]
    public async Task DeletingTheMainAuthorsPerson_LeavesTheBookWithoutAMainAuthor()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var keep = new Person(default, "Keep Author");
        var main = new Person(default, "Main Author");
        db.Persons.AddRange(keep, main);
        await db.SaveChangesAsync();
        var book = new Audiobook(
            default, "Book", null, null, null, 2024, null, null, null, null, null, null, null, null, null,
            "/library/Book/book.m4b", "book.m4b", 1000)
        {
            Authors = new List<Person> { keep, main },
            MainAuthorId = main.Id,
        };
        db.Audiobooks.Add(book);
        await db.SaveChangesAsync();

        await db.Persons.Where(p => p.Id == main.Id).ExecuteDeleteAsync();

        await using var fresh = CreateContext();
        var reloaded = await fresh.Audiobooks.AsNoTracking().Include(a => a.Authors).SingleAsync();
        Assert.IsNull(reloaded.MainAuthorId);
        Assert.AreEqual(1, reloaded.Authors.Count);
        Assert.IsNull(reloaded.MainAuthorName);
    }

    [TestMethod]
    public async Task MainAuthorName_IsReadOffTheLoadedAuthorsById()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var first = new Person(default, "First Author");
        var second = new Person(default, "Second Author");
        db.Persons.AddRange(first, second);
        await db.SaveChangesAsync();
        db.Audiobooks.Add(new Audiobook(
            default, "Book", null, null, null, 2024, null, null, null, null, null, null, null, null, null,
            "/library/Book/book.m4b", "book.m4b", 1000)
        {
            Authors = new List<Person> { first, second },
            MainAuthorId = second.Id,
        });
        await db.SaveChangesAsync();

        await using var fresh = CreateContext();
        var reloaded = await fresh.Audiobooks.AsNoTracking().Include(a => a.Authors).SingleAsync();

        Assert.AreEqual("Second Author", reloaded.MainAuthorName);
    }
}
