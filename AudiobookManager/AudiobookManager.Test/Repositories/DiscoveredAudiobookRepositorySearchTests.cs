using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class DiscoveredAudiobookRepositorySearchTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private DiscoveredAudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"discoveredrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new DiscoveredAudiobookRepository(_db);
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

    private Task SeedAsync(string fileName) =>
        _repository.InsertAsync(new DiscoveredAudiobook(
            fileName, $"/import/{fileName}", fileName, 1000, DateTime.UtcNow));

    [TestMethod]
    public async Task GetPaginatedAsync_UnaccentedSearchMatchesAccentedFileName()
    {
        // SQLite's default BINARY collation never folds diacritics, so typing "emigre" for a
        // file named "Émigré.m4b" would otherwise return nothing.
        await SeedAsync("Émigré.m4b");
        await SeedAsync("Unrelated Book.m4b");

        var (items, total) = await _repository.GetPaginatedAsync(10, 0, "emigre");

        Assert.AreEqual(1, total);
        Assert.AreEqual("Émigré.m4b", items[0].FileInfoFileName);
    }

    [TestMethod]
    public async Task GetPaginatedAsync_SearchIsCaseInsensitive()
    {
        await SeedAsync("The Great Gatsby.m4b");

        var (items, total) = await _repository.GetPaginatedAsync(10, 0, "GREAT");

        Assert.AreEqual(1, total);
        Assert.AreEqual("The Great Gatsby.m4b", items[0].FileInfoFileName);
    }
}
