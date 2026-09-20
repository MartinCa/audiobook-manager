using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>Covers AuthorSummaryFilter.Sources on GetAuthorSummariesPagedAsync.</summary>
[TestClass]
public class PersonRepositorySourceFilterTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PersonRepository _repository = null!;
    private AudiobookRepository _audiobookRepository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"personrepo-source-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PersonRepository(_db);
        _audiobookRepository = new AudiobookRepository(_db);
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

    private async Task<Person> SeedAuthorWithBookAsync(string authorName)
    {
        var book = new Audiobook(
            default, $"{authorName}'s book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{authorName}.m4b", $"{authorName}.m4b", 1000);
        var author = new Person(default, authorName);
        book.Authors.Add(author);
        await _audiobookRepository.InsertAudiobook(book);
        return author;
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_FilteredBySource_ReturnsOnlyMatchingAuthors()
    {
        var matched = await SeedAuthorWithBookAsync("Matched Author");
        await _repository.SetAuthorMatchAsync(matched.Id, "Hardcover", "hc-1", "https://hardcover.app/authors/1");
        await SeedAuthorWithBookAsync("Unmatched Author");

        var (matchedResults, matchedTotal) = await _repository.GetAuthorSummariesPagedAsync(
            null, 20, 0, new AuthorSummaryFilter(Sources: new[] { "Hardcover" }));
        Assert.AreEqual(1, matchedTotal);
        Assert.AreEqual("Matched Author", matchedResults.Single().Name);

        var (unsupportedResults, unsupportedTotal) = await _repository.GetAuthorSummariesPagedAsync(
            null, 20, 0, new AuthorSummaryFilter(Sources: new[] { AuthorSummaryFilter.UnsupportedSource }));
        Assert.AreEqual(1, unsupportedTotal);
        Assert.AreEqual("Unmatched Author", unsupportedResults.Single().Name);
    }
}
