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
        string bookName, string? www = null, string? language = null, int? durationInSeconds = null)
    {
        return await _repository.InsertAudiobook(new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, language, null, null, www, null, durationInSeconds,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000));
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
    public async Task GetAllAsync_FilteredByDurationRange_ReturnsOnlyBooksWithinRange()
    {
        await SeedBookAsync("Short book", durationInSeconds: 1800);
        await SeedBookAsync("Long book", durationInSeconds: 36000);

        var (items, total) = await _repository.GetAllAsync(
            20, 0, new BookSummaryFilter(MinDurationInSeconds: 3600));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Long book", items.Single().BookName);
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
