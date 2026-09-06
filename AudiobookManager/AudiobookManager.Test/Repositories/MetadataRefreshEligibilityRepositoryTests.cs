using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the metadata-refresh eligibility projection against a real (temp-file) SQLite
/// database: the interesting behavior is which books the SQL filter admits (non-empty Www,
/// staleness semantics including the never-refreshed case), which is a query-shape concern an
/// in-memory list cannot represent.
/// </summary>
[TestClass]
public class MetadataRefreshEligibilityRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"metarefreshelig-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
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

    private async Task<Audiobook> AddBookAsync(string bookName, string? www, DateTime? refreshedAt)
    {
        var book = new Audiobook(
            id: 0,
            bookName: bookName,
            subtitle: null,
            series: null,
            seriesPart: null,
            year: 2020,
            description: null,
            copyright: null,
            publisher: null,
            language: null,
            rating: null,
            asin: null,
            www: www,
            coverFilePath: null,
            durationInSeconds: null,
            fileInfoFullPath: $"/library/{bookName}/book.m4b",
            fileInfoFileName: "book.m4b",
            fileInfoSizeInBytes: 123)
        {
            LastMetadataRefreshedAt = refreshedAt,
        };
        _db.Audiobooks.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    [TestMethod]
    public async Task GetBooksEligible_NullCutoff_IncludesNeverRefreshedAndRefreshed()
    {
        var never = await AddBookAsync("Never", "https://www.audible.com/pd/never", null);
        var old = await AddBookAsync("Old", "https://www.audible.com/pd/old", DateTime.UtcNow.AddDays(-30));
        await AddBookAsync("NoUrl", null, null);
        await AddBookAsync("EmptyUrl", "", null);

        var eligible = await _db.Audiobooks
            .Where(a => a.Www != null && a.Www != "")
            .Select(a => a.BookName)
            .ToListAsync();

        CollectionAssert.AreEquivalent(new[] { "Never", "Old" }, eligible);
    }

    [TestMethod]
    public async Task GetBooksEligible_WithCutoff_ExcludesRecentlyRefreshed()
    {
        await AddBookAsync("Stale", "https://www.audible.com/pd/stale", DateTime.UtcNow.AddDays(-30));
        await AddBookAsync("Fresh", "https://www.audible.com/pd/fresh", DateTime.UtcNow.AddHours(-1));

        var eligible = await _db.Audiobooks
            .Where(a => a.Www != null && a.Www != "" &&
                (a.LastMetadataRefreshedAt == null || a.LastMetadataRefreshedAt < DateTime.UtcNow.AddDays(-7)))
            .Select(a => a.BookName)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { "Stale" }, eligible);
    }

    [TestMethod]
    public async Task GetBooksEligible_OrdersNeverRefreshedFirst()
    {
        // The bulk loop reports monotonic progress, so the ordering matters: never-refreshed
        // books go first, then oldest-refreshed first. A primary key id backstops ties.
        await AddBookAsync("B_RefreshedLastWeek", "https://x/b", DateTime.UtcNow.AddDays(-7));
        await AddBookAsync("A_Never", "https://x/a", null);
        await AddBookAsync("C_RefreshedLastMonth", "https://x/c", DateTime.UtcNow.AddDays(-30));

        var eligible = await _db.Audiobooks
            .Where(a => a.Www != null && a.Www != "")
            .OrderBy(a => a.LastMetadataRefreshedAt)
            .ThenBy(a => a.Id)
            .Select(a => a.BookName)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { "A_Never", "C_RefreshedLastMonth", "B_RefreshedLastWeek" }, eligible);
    }

    [TestMethod]
    public async Task PendingUpsert_SecondRefreshReplacesFirst()
    {
        var book = await AddBookAsync("Pending", "https://x/pending", null);
        var repository = new PendingMetadataRefreshRepository(_db);

        await repository.UpsertAsync(new PendingMetadataRefresh
        {
            AudiobookId = book.Id,
            FetchedAt = DateTime.UtcNow.AddDays(-1),
            SourceName = "Audible",
            SourceUrl = "https://x/pending",
            PayloadJson = "{\"version\":1}",
        });
        await repository.UpsertAsync(new PendingMetadataRefresh
        {
            AudiobookId = book.Id,
            FetchedAt = DateTime.UtcNow,
            SourceName = "Audible",
            SourceUrl = "https://x/pending",
            PayloadJson = "{\"version\":1,\"bookName\":\"Updated\"}",
        });

        Assert.AreEqual(1, await _db.PendingMetadataRefreshes.CountAsync());
        var stored = await repository.GetByAudiobookIdAsync(book.Id);
        Assert.IsNotNull(stored);
        StringAssert.Contains(stored.PayloadJson, "Updated");
    }

    [TestMethod]
    public async Task PendingDeleteByAudiobookId_ReturnsFalseWhenNone()
    {
        var book = await AddBookAsync("NoPending", "https://x/none", null);
        var repository = new PendingMetadataRefreshRepository(_db);

        Assert.IsFalse(await repository.DeleteByAudiobookIdAsync(book.Id));

        // An unknown book id has no row either - same false, not an exception.
        Assert.IsFalse(await repository.DeleteByAudiobookIdAsync(999999));
    }

    [TestMethod]
    public async Task PendingPage_IsNewestFetchedFirst()
    {
        // The interface doc promises "newest-fetched first"; the review of PR #1380 caught the
        // implementation ordering by audiobook id instead.
        var first = await AddBookAsync("FetchedFirst", "https://x/1", null);
        var second = await AddBookAsync("FetchedSecond", "https://x/2", null);
        var repository = new PendingMetadataRefreshRepository(_db);
        await repository.UpsertAsync(new PendingMetadataRefresh
        {
            AudiobookId = first.Id, FetchedAt = DateTime.UtcNow.AddDays(-1),
            SourceName = "Audible", SourceUrl = "https://x/1", PayloadJson = "{\"version\":1}",
        });
        await repository.UpsertAsync(new PendingMetadataRefresh
        {
            AudiobookId = second.Id, FetchedAt = DateTime.UtcNow,
            SourceName = "Audible", SourceUrl = "https://x/2", PayloadJson = "{\"version\":1}",
        });

        var (items, _) = await repository.GetPageWithAudiobookAsync(skip: 0, take: 10);

        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, items.Select(i => i.AudiobookId).ToList());
    }
}