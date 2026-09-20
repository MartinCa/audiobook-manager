using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// <see cref="AudiobookRepository.GetOwnedKeysByAuthorAsync"/> against real SQLite - the
/// author-reciliation counterpart of <see cref="AudiobookRepositorySeriesOwnedBooksPageTests"/>:
/// it backs <see cref="AudiobookManager.Services.AuthorReconciliationProvider"/>'s owned-key
/// matching, so the property under test is which books it includes (every book of the author -
/// series books included, so a series-linked expected entry can be matched against the same local
/// series value), the series context each key carries, and the cap/overflow contract.
/// </summary>
[TestClass]
public class AudiobookRepositoryAuthorOwnedKeysTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;
    private Person _author = null!;
    private Person _otherAuthor = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"authorowned-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
        _author = new Person(default, "Brandon Sanderson");
        _otherAuthor = new Person(default, "Robert Jordan");
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private async Task<Audiobook> SeedBookAsync(string bookName, string? series, string? seriesPart, Person author)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, seriesPart, 2024,
            null, null, null, null, null, null, null, null, 7200,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { author },
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorAsync_IncludesEveryBookOfThisAuthorWithItsSeriesContext()
    {
        await SeedBookAsync("Elantris", series: null, seriesPart: null, _author);
        await SeedBookAsync("The Way of Kings", series: "The Stormlight Archive", seriesPart: "1", _author);
        await SeedBookAsync("The Eye of the World", series: null, seriesPart: null, _otherAuthor);

        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorAsync(_author.Id, 100);

        Assert.IsFalse(overflow);
        Assert.AreEqual(2, keys.Count, "standalone AND series books are both owned-key inputs");
        var standalone = keys.Single(k => k.BookName == "Elantris");
        Assert.IsNull(standalone.SeriesPart);
        Assert.IsNull(standalone.Series);
        var seriesBook = keys.Single(k => k.BookName == "The Way of Kings");
        Assert.AreEqual("1", seriesBook.SeriesPart);
        Assert.AreEqual("The Stormlight Archive", seriesBook.Series);
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorAsync_EmptyStringSeriesKeepsItsValue()
    {
        await SeedBookAsync("Warbreaker", series: "", seriesPart: null, _author);

        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorAsync(_author.Id, 100);

        Assert.IsFalse(overflow);
        Assert.AreEqual(1, keys.Count);
        Assert.AreEqual("", keys.Single().Series,
            "the raw series value is carried verbatim - the classifier treats null/blank as standalone");
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorAsync_MoreRowsThanTheCap_ReportsOverflow()
    {
        for (var i = 0; i < 3; i++)
        {
            await SeedBookAsync($"Book {i}", series: null, seriesPart: null, _author);
        }

        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorAsync(_author.Id, 2);

        Assert.IsTrue(overflow);
        Assert.IsTrue(keys.Count >= 2);
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorAsync_UnknownAuthor_ReturnsEmpty()
    {
        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorAsync(999, 100);

        Assert.AreEqual(0, keys.Count);
        Assert.IsFalse(overflow);
    }

    // The batched bulk-filter read that removed the N+1: ONE query must serve every rostered
    // author's owned keys, grouped by person id with the same series/part/title context the
    // single-author read carries, and never include a book of an author nobody asked about - the
    // backend of GetBulkMissingOrUpcomingAuthorIdsAsync, so any author whose owned key could be
    // lost from this grouping risks a wrongly-flagged book.
    [TestMethod]
    public async Task GetOwnedKeysByAuthorsAsync_ReturnsEveryRequestedAuthorsKeysGroupedInOneQuery()
    {
        await SeedBookAsync("Elantris", series: null, seriesPart: null, _author);
        await SeedBookAsync("The Way of Kings", series: "The Stormlight Archive", seriesPart: "1", _author);
        await SeedBookAsync("The Eye of the World", series: null, seriesPart: null, _otherAuthor);
        await SeedBookAsync("Alanna", series: null, seriesPart: null, new Person(default, "Tamora Pierce"));

        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorsAsync(
            new List<long> { _author.Id, _otherAuthor.Id }, 100);

        Assert.IsFalse(overflow);
        var byAuthor = keys.GroupBy(k => k.PersonId).ToDictionary(g => g.Key, g => g.ToList());
        Assert.AreEqual(2, byAuthor[_author.Id].Count, "both of the author's books - series and standalone");
        Assert.AreEqual(1, byAuthor[_otherAuthor.Id].Count);
        Assert.AreEqual(2, byAuthor.Count, "a person nobody requested must not appear");
        var seriesKey = byAuthor[_author.Id].Single(k => k.Key.BookName == "The Way of Kings").Key;
        Assert.AreEqual("1", seriesKey.SeriesPart);
        Assert.AreEqual("The Stormlight Archive", seriesKey.Series);
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorsAsync_MoreRowsThanTheTotalCap_ReportsOverflow()
    {
        for (var i = 0; i < 3; i++)
        {
            await SeedBookAsync($"Book {i}", series: null, seriesPart: null, _author);
        }

        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorsAsync(
            new List<long> { _author.Id }, 2);

        Assert.IsTrue(overflow, "a total past the cap must be reported, never silently truncated");
        Assert.AreEqual(3, keys.Count, "the cap + 1 probe row comes back so the caller can detect the breach");
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorsAsync_UnknownPersons_ReturnsEmpty()
    {
        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorsAsync(
            new List<long> { 999, 1000 }, 100);

        Assert.AreEqual(0, keys.Count);
        Assert.IsFalse(overflow);
    }

    [TestMethod]
    public async Task GetOwnedKeysByAuthorsAsync_EmptyPersonList_ReturnsEmptyWithoutQuerying()
    {
        var (keys, overflow) = await _repository.GetOwnedKeysByAuthorsAsync(new List<long>(), 100);

        Assert.AreEqual(0, keys.Count);
        Assert.IsFalse(overflow);
    }
}