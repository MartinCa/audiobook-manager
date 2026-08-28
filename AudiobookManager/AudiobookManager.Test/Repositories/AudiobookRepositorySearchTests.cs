using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class AudiobookRepositorySearchTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"audiobookrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
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

    private async Task SeedBookAsync(string bookName, string? series)
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000));
    }

    [TestMethod]
    public async Task SearchSeriesAsync_GroupsMatchingSeriesWithBookCounts()
    {
        await SeedBookAsync("Mistborn 1", "Mistborn");
        await SeedBookAsync("Mistborn 2", "Mistborn");
        await SeedBookAsync("Elantris", null);
        await SeedBookAsync("Something Else", "Wheel of Time");

        var results = await _repository.SearchSeriesAsync("mist", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Mistborn", results[0].Series);
        Assert.AreEqual(2, results[0].BookCount);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_ReturnsEmptyWhenNoSeriesMatch()
    {
        await SeedBookAsync("Elantris", null);
        await SeedBookAsync("Something Else", "Wheel of Time");

        var results = await _repository.SearchSeriesAsync("nonexistent", 10);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_UnaccentedQueryMatchesAccentedSeriesName()
    {
        // SQLite's default BINARY collation (which LIKE uses) never folds diacritics, so typing
        // "cafe" for "Café" would otherwise return nothing.
        await SeedBookAsync("Café Noir 1", "Café Noir");

        var results = await _repository.SearchSeriesAsync("cafe", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Café Noir", results[0].Series);
    }

    [TestMethod]
    public async Task SearchAsync_UnaccentedQueryMatchesAccentedBookName()
    {
        await SeedBookAsync("Émigré", null);

        var (items, total) = await _repository.SearchAsync("emigre", 10, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Émigré", items[0].BookName);
    }

    [TestMethod]
    public async Task SearchAsync_AccentedQueryMatchesUnaccentedBookName()
    {
        await SeedBookAsync("Emigre", null);

        var (items, total) = await _repository.SearchAsync("émigré", 10, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Emigre", items[0].BookName);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_RespectsLimit()
    {
        await SeedBookAsync("Book A", "Series Alpha");
        await SeedBookAsync("Book B", "Series Beta");
        await SeedBookAsync("Book C", "Series Gamma");

        var results = await _repository.SearchSeriesAsync("series", 2);

        Assert.AreEqual(2, results.Count);
    }

    [TestMethod]
    public async Task GetByFullPathAsync_MatchingBookExists_ReturnsIt()
    {
        await SeedBookAsync("Children of Time", null);

        var result = await _repository.GetByFullPathAsync("/library/Children of Time.m4b");

        Assert.IsNotNull(result);
        Assert.AreEqual("Children of Time", result.BookName);
    }

    [TestMethod]
    public async Task GetByFullPathAsync_NoBookAtThatPath_ReturnsNull()
    {
        await SeedBookAsync("Children of Time", null);

        var result = await _repository.GetByFullPathAsync("/library/Someone Else.m4b");

        Assert.IsNull(result);
    }
}
