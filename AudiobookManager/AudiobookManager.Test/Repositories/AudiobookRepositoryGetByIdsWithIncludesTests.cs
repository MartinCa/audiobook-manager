using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The bulk flows (bulk edit, selected refresh, selected recheck) load their books through
/// GetByIdsWithIncludesAsync: id-bounded, id-ordered, with the Authors/Narrators/Genres graph.
/// </summary>
[TestClass]
public class AudiobookRepositoryGetByIdsWithIncludesTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"audiobookbyids-{Guid.NewGuid():N}.db");
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

    private async Task<Audiobook> SeedAsync(string bookName, bool withGraphs = false)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);

        if (withGraphs)
        {
            audiobook.Authors.Add(new Person(default, "An Author"));
            audiobook.Narrators.Add(new Person(default, "A Narrator"));
            audiobook.Genres.Add(new Genre(default, "Fantasy"));
        }

        return await _repository.InsertAudiobook(audiobook);
    }

    [TestMethod]
    public async Task GetByIdsWithIncludesAsync_ReturnsOnlyTheRequestedBooks_OrderedById_WithGraphsLoaded()
    {
        // Insert order differs from id order, so an ORDER BY id violation would be visible.
        await SeedAsync("Book A", withGraphs: true); // id 1
        var bookB = await SeedAsync("Book B");       // id 2
        var bookC = await SeedAsync("Book C");       // id 3

        // Book B requested too, so the id-ordered outcome has to interleave it before C.
        var books = await _repository.GetByIdsWithIncludesAsync(new List<long> { bookB.Id, 3, 1 });

        Assert.AreEqual(3, books.Count, "every requested id that resolves is returned");
        Assert.AreEqual(1, books[0].Id);
        Assert.AreEqual("Book A", books[0].BookName);
        Assert.AreEqual(2, books[1].Id);
        Assert.AreEqual(3, books[2].Id);

        var withGraphs = books.First(b => b.Id == 1);
        // AsNoTracking + AsSplitQuery: the relation tables are separate queries, but the graph
        // must come back as though it were one - that is the point of this loading shape.
        Assert.AreEqual(1, withGraphs.Authors.Count);
        Assert.AreEqual("An Author", withGraphs.Authors[0].Name);
        Assert.AreEqual("A Narrator", withGraphs.Narrators.Single().Name);
        Assert.AreEqual("Fantasy", withGraphs.Genres.Single().Name);
    }

    [TestMethod]
    public async Task GetByIdsWithIncludesAsync_MissingIds_AreAbsentFromTheResult()
    {
        await SeedAsync("Book A");

        var books = await _repository.GetByIdsWithIncludesAsync(new List<long> { 1, 99, 100 });

        Assert.AreEqual(1, books.Count);
        Assert.AreEqual(1, books[0].Id);
    }

    [TestMethod]
    public async Task GetByIdsWithIncludesAsync_EmptySelection_ReturnsEmpty()
    {
        var books = await _repository.GetByIdsWithIncludesAsync(new List<long>());

        Assert.AreEqual(0, books.Count);
    }
}