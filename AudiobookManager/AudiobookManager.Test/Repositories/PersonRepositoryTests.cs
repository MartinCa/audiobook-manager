using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the author standalone-books roster path against a real (temp-file) SQLite database,
/// mirroring <see cref="SeriesRepositoryTests"/> - the point of the natural-key (title) addressing
/// is exactly what happens to row ids across a roster replace, which an in-memory fake cannot
/// disprove.
/// </summary>
[TestClass]
public class PersonRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PersonRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"personrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PersonRepository(_db);
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

    private async Task<Person> SeedAuthorAsync()
    {
        var author = await _repository.GetOrCreatePerson("Brandon Sanderson");

        await _repository.ReplaceAuthorExpectedBooksAsync(author.Id, new List<AuthorExpectedBook>
        {
            new() { Title = "Elantris", Year = 2005 },
            new() { Title = "Warbreaker", Year = 2009 },
        });

        return author;
    }

    [TestMethod]
    public async Task GetByIdWithExpectedBooksBoundedAsync_ReturnsThePersonAndItsRoster()
    {
        var author = await SeedAuthorAsync();

        var (result, overflow) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);

        Assert.IsNotNull(result);
        Assert.AreEqual(author.Id, result.Id);
        Assert.AreEqual(2, result.ExpectedBooks.Count);
        Assert.IsFalse(overflow);
    }

    [TestMethod]
    public async Task GetByIdWithExpectedBooksBoundedAsync_UnknownPerson_ReturnsNull()
    {
        var (result, overflow) = await _repository.GetByIdWithExpectedBooksBoundedAsync(999, 10);

        Assert.IsNull(result);
        Assert.IsFalse(overflow);
    }

    [TestMethod]
    public async Task GetByIdWithExpectedBooksBoundedAsync_MoreRowsThanTheCap_ReportsOverflow()
    {
        var author = await SeedAuthorAsync();

        var (result, overflow) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 1);

        Assert.IsNotNull(result);
        // The overflow flag is what the caller acts on; the fetch is bounded to cap + 1 rows, so
        // the returned collection itself may exceed the cap by one - only the flag matters.
        Assert.IsTrue(overflow);
    }

    [TestMethod]
    public async Task ReplaceAuthorExpectedBooksAsync_ReplacesTheWholeRoster()
    {
        var author = await SeedAuthorAsync();

        await _repository.ReplaceAuthorExpectedBooksAsync(author.Id, new List<AuthorExpectedBook>
        {
            new() { Title = "The Way of Kings", Year = 2010 },
        });

        var (result, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.ExpectedBooks.Count);
        Assert.AreEqual("The Way of Kings", result.ExpectedBooks.Single().Title);
    }

    [TestMethod]
    public async Task ReplaceAuthorExpectedBooksAsync_TrackedDeleteThenInsert_DoesNotLeakOldRowsAcrossAuthors()
    {
        var author = await SeedAuthorAsync();
        var otherAuthor = await _repository.GetOrCreatePerson("Robert Jordan");
        await _repository.ReplaceAuthorExpectedBooksAsync(otherAuthor.Id, new List<AuthorExpectedBook>
        {
            new() { Title = "The Eye of the World" },
        });

        await _repository.ReplaceAuthorExpectedBooksAsync(author.Id, new List<AuthorExpectedBook>());

        var (authorResult, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);
        var (otherResult, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(otherAuthor.Id, 10);
        Assert.IsNotNull(authorResult);
        Assert.AreEqual(0, authorResult.ExpectedBooks.Count);
        Assert.IsNotNull(otherResult);
        Assert.AreEqual(1, otherResult.ExpectedBooks.Count, "replacing one author's roster must not touch another's");
    }

    [TestMethod]
    public async Task SetAuthorExpectedBookIgnoredAsync_FlagsTheEntryMatchingTheNaturalKey()
    {
        var author = await SeedAuthorAsync();

        await _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "Elantris", true);

        var (result, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);
        Assert.IsNotNull(result);
        Assert.IsTrue(result.ExpectedBooks.Single(b => b.Title == "Elantris").IsIgnored);
        Assert.IsFalse(result.ExpectedBooks.Single(b => b.Title == "Warbreaker").IsIgnored);
    }

    [TestMethod]
    public async Task SetAuthorExpectedBookIgnoredAsync_MatchesCaseInsensitivelyAndTrimmed()
    {
        var author = await SeedAuthorAsync();

        await _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "  ELANTRIS  ", true);

        var (result, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);
        Assert.IsNotNull(result);
        Assert.IsTrue(result.ExpectedBooks.Single(b => b.Title == "Elantris").IsIgnored);
    }

    [TestMethod]
    public async Task SetAuthorExpectedBookIgnoredAsync_UnknownTitle_ThrowsKeyNotFound()
    {
        var author = await SeedAuthorAsync();

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "Nonexistent", true));
    }

    [TestMethod]
    public async Task SetAuthorExpectedBookIgnoredAsync_FalseUnignoresAPreviouslyIgnoredEntry()
    {
        var author = await SeedAuthorAsync();
        await _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "Elantris", true);

        await _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "Elantris", false);

        var (result, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);
        Assert.IsNotNull(result);
        Assert.IsFalse(result.ExpectedBooks.Single(b => b.Title == "Elantris").IsIgnored);
    }

    [TestMethod]
    public async Task SetAuthorExpectedBookIgnoredAsync_StillHitsTheSameLogicalBookAfterARosterReplace()
    {
        var author = await SeedAuthorAsync();
        await _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "Elantris", true);

        // A refresh deletes and re-inserts the whole roster - the row id changes, but the title
        // (the natural key) is the same logical book.
        await _repository.ReplaceAuthorExpectedBooksAsync(author.Id, new List<AuthorExpectedBook>
        {
            new() { Title = "Elantris", Year = 2005, IsIgnored = true },
            new() { Title = "Warbreaker", Year = 2009 },
        });

        await _repository.SetAuthorExpectedBookIgnoredAsync(author.Id, "Elantris", false);

        var (result, _) = await _repository.GetByIdWithExpectedBooksBoundedAsync(author.Id, 10);
        Assert.IsNotNull(result);
        Assert.IsFalse(result.ExpectedBooks.Single(b => b.Title == "Elantris").IsIgnored);
    }
}
