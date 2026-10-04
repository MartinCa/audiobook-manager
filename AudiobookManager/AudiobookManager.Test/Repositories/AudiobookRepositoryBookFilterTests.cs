using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Covers BookSummaryFilter (source/genre/language/duration) on GetAllAsync, and in particular
/// that Audiobook.MatchedSourceName is actually derived from Www by
/// AccentFoldedColumnsInterceptor on insert - the save interceptor this filter depends on to have
/// anything to query.
/// </summary>
[TestClass]
public class AudiobookRepositoryBookFilterTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"audiobookrepo-filter-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        var options = new DbContextOptionsBuilder<DatabaseContext>().Options;
        _db = new DatabaseContext(options, settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<Audiobook> SeedBookAsync(
        string bookName, string? www = null, string? language = null, int? durationInSeconds = null,
        string qualifiers = "")
    {
        var book = await _repository.InsertAudiobook(new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, language, null, null, www, null, durationInSeconds,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000));
        if (qualifiers.Length > 0)
        {
            await _db.Audiobooks.Where(a => a.Id == book.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Qualifiers, qualifiers));
        }

        return book;
    }

    [TestMethod]
    public async Task InsertAudiobook_WwwFromKnownSource_DerivesMatchedSourceName()
    {
        var book = await SeedBookAsync("Book", www: "https://www.audible.com/pd/12345");

        var reloaded = await _db.Audiobooks.AsNoTracking().SingleAsync(a => a.Id == book.Id);
        Assert.AreEqual("Audible", reloaded.MatchedSourceName);
    }

    [TestMethod]
    public async Task InsertAudiobook_WwwFromUnknownHost_LeavesMatchedSourceNameNull()
    {
        var book = await SeedBookAsync("Book", www: "https://www.example.com/book");

        var reloaded = await _db.Audiobooks.AsNoTracking().SingleAsync(a => a.Id == book.Id);
        Assert.IsNull(reloaded.MatchedSourceName);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredBySource_ReturnsOnlyMatchingBooks()
    {
        await SeedBookAsync("Audible book", www: "https://www.audible.com/pd/1");
        await SeedBookAsync("Goodreads book", www: "https://www.goodreads.com/book/2");
        await SeedBookAsync("No source book");

        var (audibleOnly, audibleTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Sources: new[] { "Audible" }));
        Assert.AreEqual(1, audibleTotal);
        Assert.AreEqual("Audible book", audibleOnly.Single().BookName);

        var (unsupportedOnly, unsupportedTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Sources: new[] { BookSummaryFilter.UnsupportedSource }));
        Assert.AreEqual(1, unsupportedTotal);
        Assert.AreEqual("No source book", unsupportedOnly.Single().BookName);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByLanguage_ReturnsOnlyMatchingBooks()
    {
        await SeedBookAsync("English book", language: "English");
        await SeedBookAsync("French book", language: "French");

        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Languages: new[] { "French" }));

        Assert.AreEqual(1, total);
        Assert.AreEqual("French book", items.Single().BookName);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByQualifiers_MatchesAnySelectedKeyExactly()
    {
        await SeedBookAsync("Plain book");
        await SeedBookAsync("Abridged book", qualifiers: ",abridged,");
        await SeedBookAsync("Dramatized book", qualifiers: ",dramatized,");
        await SeedBookAsync("Both book", qualifiers: ",abridged,dramatized,");

        var (abridged, abridgedTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Qualifiers: new[] { "abridged" }));
        CollectionAssert.AreEquivalent(
            new[] { "Abridged book", "Both book" }, abridged.Select(b => b.BookName).ToArray());
        Assert.AreEqual(2, abridgedTotal);

        var (either, _) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Qualifiers: new[] { "abridged", "dramatized" }));
        Assert.AreEqual(3, either.Count);

        // A key that is merely a substring of a stored key must not match.
        var (partial, partialTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Qualifiers: new[] { "abridge" }));
        Assert.AreEqual(0, partialTotal);
        Assert.AreEqual(0, partial.Count);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByNoQualifier_ReturnsOnlyBooksWithoutQualifiers()
    {
        await SeedBookAsync("Plain book");
        await SeedBookAsync("Abridged book", qualifiers: ",abridged,");

        var (none, noneTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Qualifiers: new[] { BookSummaryFilter.NoQualifier }));
        Assert.AreEqual(1, noneTotal);
        Assert.AreEqual("Plain book", none.Single().BookName);

        var (noneOrAbridged, _) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Qualifiers: new[] { BookSummaryFilter.NoQualifier, "abridged" }));
        Assert.AreEqual(2, noneOrAbridged.Count);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByDurationRange_ReturnsOnlyBooksWithinRange()
    {
        await SeedBookAsync("Short book", durationInSeconds: 1800);
        await SeedBookAsync("Long book", durationInSeconds: 36000);

        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(MinDurationInSeconds: 3600));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Long book", items.Single().BookName);
    }

    private async Task<(string Never, string Old, string Recent)> SeedRefreshedBooksAsync()
    {
        var never = await SeedBookAsync("Never refreshed");
        var old = await SeedBookAsync("Old refresh");
        var recent = await SeedBookAsync("Recent refresh");
        await _repository.UpdateLastMetadataRefreshedAtAsync(old.Id, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        await _repository.UpdateLastMetadataRefreshedAtAsync(recent.Id, new DateTime(2026, 6, 20, 23, 30, 0, DateTimeKind.Utc));
        return (never.BookName, old.BookName, recent.BookName);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByNeverRefreshed_SplitsOnTheRefreshStamp()
    {
        await SeedRefreshedBooksAsync();

        var (never, neverTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(NeverRefreshed: true));
        Assert.AreEqual(1, neverTotal);
        Assert.AreEqual("Never refreshed", never.Single().BookName);

        var (refreshed, refreshedTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(NeverRefreshed: false));
        Assert.AreEqual(2, refreshedTotal);
        CollectionAssert.AreEquivalent(
            new[] { "Old refresh", "Recent refresh" }, refreshed.Select(a => a.BookName).ToList());
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByRefreshedAfter_IsInclusiveAndExcludesNeverRefreshed()
    {
        await SeedRefreshedBooksAsync();

        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(RefreshedAfter: new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc)));

        Assert.AreEqual(2, total);
        CollectionAssert.AreEquivalent(
            new[] { "Old refresh", "Recent refresh" }, items.Select(a => a.BookName).ToList());

        var (later, laterTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(RefreshedAfter: new DateTime(2026, 2, 1)));
        Assert.AreEqual(1, laterTotal);
        Assert.AreEqual("Recent refresh", later.Single().BookName);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByRefreshedBefore_UsesTheExactInstantAsAnExclusiveBound()
    {
        await SeedRefreshedBooksAsync();

        // "Recent refresh" is stamped 2026-06-20 23:30:00Z. A never-refreshed book never
        // satisfies a date bound.
        var (atStamp, atStampTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(RefreshedBefore: new DateTime(2026, 6, 20, 23, 30, 0, DateTimeKind.Utc)));
        Assert.AreEqual(1, atStampTotal, "an exclusive bound excludes a refresh stamped exactly at it");
        Assert.AreEqual("Old refresh", atStamp.Single().BookName);

        var (afterStamp, afterStampTotal) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(RefreshedBefore: new DateTime(2026, 6, 20, 23, 31, 0, DateTimeKind.Utc)));
        Assert.AreEqual(2, afterStampTotal);
        CollectionAssert.AreEquivalent(
            new[] { "Old refresh", "Recent refresh" }, afterStamp.Select(a => a.BookName).ToList());
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByRefreshedAfter_HonoursTimeOfDay()
    {
        await SeedRefreshedBooksAsync();

        // "Old refresh" is stamped 2026-01-10 12:00:00Z: a bound a minute later excludes it.
        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(RefreshedAfter: new DateTime(2026, 1, 10, 12, 1, 0, DateTimeKind.Utc)));
        Assert.AreEqual(1, total);
        Assert.AreEqual("Recent refresh", items.Single().BookName);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByRefreshedBefore_ConvertsALocalKindBoundToUtc()
    {
        await SeedRefreshedBooksAsync();

        // ASP.NET binds a "...Z" timestamp to a Local-kind DateTime; the same instant must filter
        // identically whatever the server's zone is.
        var localKind = new DateTime(2026, 6, 20, 23, 31, 0, DateTimeKind.Utc).ToLocalTime();
        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(RefreshedBefore: localKind));
        Assert.AreEqual(2, total);
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public async Task SearchAsync_FilteredByRefreshedRange_NarrowsTheTextSearch()
    {
        await SeedRefreshedBooksAsync();

        var (items, total) = await _repository.SearchAsync(
            "refresh", 20, 0,
            filter: new BookSummaryFilter(
                RefreshedAfter: new DateTime(2026, 1, 1), RefreshedBefore: new DateTime(2026, 3, 1)));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Old refresh", items.Single().BookName);
    }

    [TestMethod]
    public void IsEmpty_FalseForAnyRefreshedField()
    {
        Assert.IsTrue(new BookSummaryFilter().IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(NeverRefreshed: true).IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(RefreshedAfter: DateTime.UtcNow).IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(RefreshedBefore: DateTime.UtcNow).IsEmpty);
    }

    [TestMethod]
    public async Task GetAllAsync_FilteredByGenre_ReturnsOnlyMatchingBooks()
    {
        var fantasy = await SeedBookAsync("Fantasy book");
        fantasy.Genres.Add(new Genre(default, "Fantasy"));
        await SeedBookAsync("Unrelated book");
        await _db.SaveChangesAsync();

        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(Genres: new[] { "Fantasy" }));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Fantasy book", items.Single().BookName);
    }
}
