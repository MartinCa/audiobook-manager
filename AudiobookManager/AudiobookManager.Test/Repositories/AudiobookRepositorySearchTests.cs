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

    // Regression test: ranking used to happen in BrowseController, *after* the repository had
    // already truncated to `limit` in plain alphabetical order - so the one series that actually
    // starts with the query was discarded before anything could rank it. Fails against the
    // pre-fix repository, which returns the five alphabetically-first matches and never
    // includes "Harry Potter".
    [TestMethod]
    public async Task SearchSeriesAsync_RanksPrefixMatchesBeforeApplyingTheLimit()
    {
        // All six contain "charm", so the LIKE matches every one of them and the limit of 5 has
        // to drop one. Only "charm school" *starts* with it - and its lowercase initial sorts
        // after every uppercase one under SQLite's BINARY collation, so plain alphabetical
        // ordering is exactly what drops it.
        foreach (var series in new[] { "Alex Rider charm", "Bosch charm", "Cormoran charm", "Dresden charm", "Expanse charm" })
        {
            await SeedBookAsync($"{series} book", series);
        }
        await SeedBookAsync("charm school book", "charm school");

        var results = await _repository.SearchSeriesAsync("charm", 5);

        Assert.AreEqual(5, results.Count);
        Assert.AreEqual("charm school", results[0].Series);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_PrefixMatchIsRankedFirstAmongSeveralMatches()
    {
        await SeedBookAsync("b", "The Sanderson Files");
        await SeedBookAsync("c", "Sanditon");

        var results = await _repository.SearchSeriesAsync("sand", 5);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("Sanditon", results[0].Series);
        Assert.AreEqual("The Sanderson Files", results[1].Series);
    }

    // Same truncation bug on the book search: the type-ahead asks for 5 rows, and ordering by
    // title alone threw away the prefix match before the controller could rank it.
    [TestMethod]
    public async Task SearchAsync_RanksPrefixMatchesBeforeApplyingTheLimit()
    {
        foreach (var title in new[] { "Alex charm", "Bosch charm", "Cormoran charm", "Dresden charm", "Expanse charm" })
        {
            await SeedBookAsync(title, null);
        }
        await SeedBookAsync("Charm School", null);

        var (items, total) = await _repository.SearchAsync("charm", 5, 0);

        Assert.AreEqual(6, total);
        Assert.AreEqual(5, items.Count);
        Assert.AreEqual("Charm School", items[0].BookName);
    }

    // Regression test: this compared paths with a raw SQL `==`, which SQLite evaluates under its
    // BINARY collation - so on a case-insensitive volume a stored path differing only in case
    // from the generated target (an author's casing edited in the tags since import) was not
    // found, and the duplicate check reported an untracked file instead of the tracked book.
    // Fails against the pre-fix repository, which returns null.
    [TestMethod]
    public async Task GetByFullPathAsync_CaseDifferingStoredPath_IsFoundWithACaseInsensitiveComparison()
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, "Children of Time", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/Adrian Tchaikovsky/Children of Time.m4b", "Children of Time.m4b", 1000));

        var result = await _repository.GetByFullPathAsync(
            "/library/adrian tchaikovsky/children of time.m4b",
            (a, b) => string.Equals(
                Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase));

        Assert.IsNotNull(result);
        Assert.AreEqual("Children of Time", result.BookName);
    }

    [TestMethod]
    public async Task GetByFullPathAsync_CaseDifferingStoredPath_IsNotFoundWithACaseSensitiveComparison()
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, "Children of Time", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/Adrian Tchaikovsky/Children of Time.m4b", "Children of Time.m4b", 1000));

        var result = await _repository.GetByFullPathAsync(
            "/library/adrian tchaikovsky/children of time.m4b",
            (a, b) => string.Equals(a, b, StringComparison.Ordinal));

        Assert.IsNull(result);
    }

    // A literal '_' in a file name (extremely common) must not act as a LIKE wildcard and pull
    // in a different book that happens to differ in that one position.
    [TestMethod]
    public async Task GetByFullPathAsync_UnderscoreInFileName_IsNotTreatedAsAWildcard()
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, "Wrong Book", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/aXb.m4b", "aXb.m4b", 1000));

        var result = await _repository.GetByFullPathAsync(
            "/library/a_b.m4b",
            (a, b) => string.Equals(a, b, StringComparison.Ordinal));

        Assert.IsNull(result);
    }

    // Regression test: UpdateAudiobookAsync called DbSet.Update() on an entity the caller had
    // just loaded from this same context. On the *root* that marks every property modified
    // regardless of what changed, so saving one book rewrote all ~18 columns - the whole
    // description blob included - instead of the one field the user edited.
    //
    // (Related entities already tracked are not affected: EF's graph traversal only paints
    // nodes whose state is Detached. An earlier version of this test asserted otherwise and
    // passed against the unfixed code, which is why it now asserts on the root's property
    // states - and those are sampled during SaveChanges, since it resets them afterwards.)
    [TestMethod]
    public async Task UpdateAudiobookAsync_TrackedEntity_OnlyWritesTheColumnsThatChanged()
    {
        var person = new Person(default, "Adrian Tchaikovsky");
        var book = new Audiobook(
            default, "Children of Time", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/Children of Time.m4b", "Children of Time.m4b", 1000);
        book.Authors.Add(person);
        await _repository.InsertAudiobook(book);

        var loaded = await _repository.GetByIdWithIncludesAsync(book.Id);
        Assert.IsNotNull(loaded);
        loaded.BookName = "Children of Time (Revised)";

        List<string> modifiedProperties = null!;
        void CaptureModifiedProperties(object? sender, SavingChangesEventArgs args) =>
            modifiedProperties = _db.Entry(loaded).Properties
                .Where(prop => prop.IsModified)
                .Select(prop => prop.Metadata.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        _db.SavingChanges += CaptureModifiedProperties;
        try
        {
            await _repository.UpdateAudiobookAsync(loaded);
        }
        finally
        {
            _db.SavingChanges -= CaptureModifiedProperties;
        }

        Assert.IsNotNull(modifiedProperties, "SaveChanges should have run");
        CollectionAssert.AreEqual(new[] { nameof(Audiobook.BookName) }, modifiedProperties);

        // ...and the edit itself still persisted.
        var reread = await _repository.GetByIdWithIncludesAsync(book.Id);
        Assert.AreEqual("Children of Time (Revised)", reread!.BookName);
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
