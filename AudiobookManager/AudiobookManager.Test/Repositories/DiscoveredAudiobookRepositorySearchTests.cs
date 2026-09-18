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

    // Regression: this search interpolated the folded user query straight into the LIKE pattern
    // with no escape character, so a '%' or '_' the user typed acted as a wildcard - '%' matched
    // every discovered file. It was the one search path left out when LikePatterns centralized
    // the escaping, which is the failure that helper exists to prevent.
    [TestMethod]
    public async Task GetPaginatedAsync_LikeWildcardsInTheSearch_AreTreatedLiterally()
    {
        await SeedAsync("B_eta Helper.m4b");
        await SeedAsync("Breta Helper.m4b");
        await SeedAsync("100% Author.m4b");
        await SeedAsync("100 Friends.m4b");

        var (underscore, totalUnderscore) = await _repository.GetPaginatedAsync(10, 0, "B_eta");
        CollectionAssert.AreEqual(
            new[] { "B_eta Helper.m4b" },
            underscore.Select(d => d.FileInfoFileName).ToList(),
            "an underscore in the search matches a literal underscore, not 'any character'");
        Assert.AreEqual(1, totalUnderscore);

        var (percent, totalPercent) = await _repository.GetPaginatedAsync(10, 0, "100%");
        CollectionAssert.AreEqual(
            new[] { "100% Author.m4b" },
            percent.Select(d => d.FileInfoFileName).ToList(),
            "'%' in the search matches a literal '%', not a wildcard");
        Assert.AreEqual(1, totalPercent);
    }

    // Not a regression guard for the missing-escape bug above - this passed before the fix too,
    // because SQLite's LIKE treats a backslash literally when no ESCAPE clause is given. It guards
    // the obligation the fix created: now that '\\' is declared as the ESCAPE character, a
    // backslash the user typed has to be doubled by EscapeLikePattern or it escapes the next
    // character instead of matching itself. Dropping that Replace turns this red (the search then
    // returns "ACDC Live.m4b" rather than the file the user actually typed).
    [TestMethod]
    public async Task GetPaginatedAsync_ABackslashInTheSearch_IsTreatedLiterally()
    {
        await SeedAsync("AC\\DC Live.m4b");
        await SeedAsync("ACDC Live.m4b");

        var (items, total) = await _repository.GetPaginatedAsync(10, 0, "AC\\DC");

        CollectionAssert.AreEqual(
            new[] { "AC\\DC Live.m4b" },
            items.Select(d => d.FileInfoFileName).ToList());
        Assert.AreEqual(1, total);
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
