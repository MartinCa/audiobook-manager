using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises PersonRepository's paged author-summary queries and filter support against a real
/// (temp-file) SQLite database.
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

    private async Task<Person> SeedAuthorWithBooksAsync(string name, int bookCount)
    {
        var author = new Person(default, name);
        for (var i = 0; i < bookCount; i++)
        {
            _db.Audiobooks.Add(new Audiobook(
                default, $"{name} Book {i}", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                $"/library/{name}-{i}.m4b", $"{name}-{i}.m4b", 1000)
            {
                Authors = new List<Person> { author },
            });
        }

        await _db.SaveChangesAsync();
        return author;
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_FollowedFilter_IncludesOnlyFollowedAuthors()
    {
        var followed = await SeedAuthorWithBooksAsync("Followed Author", 1);
        await SeedAuthorWithBooksAsync("Other Author", 1);
        _db.AuthorFollows.Add(new AuthorFollow { PersonId = followed.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0, filter: new AuthorSummaryFilter(Followed: true));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Followed Author", items.Single().Name);
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_MatchedFilter_IncludesOnlyMatchedAuthors()
    {
        var matched = await SeedAuthorWithBooksAsync("Matched Author", 1);
        matched.MatchedSourceId = "hc-1";
        await SeedAuthorWithBooksAsync("Unmatched Author", 1);
        await _db.SaveChangesAsync();

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0, filter: new AuthorSummaryFilter(Matched: true));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Matched Author", items.Single().Name);
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_MinBookCountFilter_ExcludesAuthorsWithFewerBooks()
    {
        await SeedAuthorWithBooksAsync("Prolific Author", 3);
        await SeedAuthorWithBooksAsync("One Book Author", 1);

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0, filter: new AuthorSummaryFilter(MinBookCount: 2));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Prolific Author", items.Single().Name);
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_NeverRefreshedFilter_IncludesOnlyNullLastRefreshedAt()
    {
        var refreshed = await SeedAuthorWithBooksAsync("Refreshed Author", 1);
        refreshed.LastRefreshedAt = DateTime.UtcNow;
        await SeedAuthorWithBooksAsync("Unrefreshed Author", 1);
        await _db.SaveChangesAsync();

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0, filter: new AuthorSummaryFilter(NeverRefreshed: true));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Unrefreshed Author", items.Single().Name);
    }

    // Regression: the UI sends a calendar date (day granularity), which model-binds to that
    // day's midnight. A plain "<=" against that midnight used to exclude every refresh later
    // that same day, so picking "before 2024-06-01" silently dropped an author refreshed at
    // 2024-06-01T15:00 - asymmetric with RefreshedAfter's inclusive ">=" against the same day's
    // midnight, which does include the whole day.
    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_RefreshedBeforeFilter_IncludesRefreshesLaterThatSameDay()
    {
        var sameDayLater = await SeedAuthorWithBooksAsync("Same Day Author", 1);
        sameDayLater.LastRefreshedAt = new DateTime(2024, 6, 1, 15, 30, 0, DateTimeKind.Utc);
        var nextDay = await SeedAuthorWithBooksAsync("Next Day Author", 1);
        nextDay.LastRefreshedAt = new DateTime(2024, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        await _db.SaveChangesAsync();

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0,
            filter: new AuthorSummaryFilter(RefreshedBefore: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Same Day Author", items.Single().Name);
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_RestrictToIds_NarrowsTheResult()
    {
        var keep = await SeedAuthorWithBooksAsync("Keep Author", 1);
        await SeedAuthorWithBooksAsync("Drop Author", 1);

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0, restrictToIds: new[] { keep.Id });

        Assert.AreEqual(1, total);
        Assert.AreEqual("Keep Author", items.Single().Name);
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_ExcludeIds_RemovesMatchingAuthors()
    {
        await SeedAuthorWithBooksAsync("Keep Author", 1);
        var drop = await SeedAuthorWithBooksAsync("Drop Author", 1);

        var (items, total) = await _repository.GetAuthorSummariesPagedAsync(
            null, 10, 0, excludeIds: new[] { drop.Id });

        Assert.AreEqual(1, total);
        Assert.AreEqual("Keep Author", items.Single().Name);
    }

    // The batched roster-author resolution (SeriesService.MatchSeriesCoreAsync): one call resolves
    // a whole set of source author spellings against the library's Person rows. Exact-match on the
    // unique name - a name with no Person row is absent from the result, never a created row.
    [TestMethod]
    public async Task GetByNamesAsync_ResolvesExistingNamesAndOmitsUnknownOnes()
    {
        var existing = Enumerable.Range(0, 20)
            .Select(i => new Person(default, $"Author {i:D2}"))
            .ToList();
        _db.Persons.AddRange(existing);
        await _db.SaveChangesAsync();

        var resolved = await _repository.GetByNamesAsync(
            existing.Select(p => p.Name).Concat(new[] { "Author 99", "Missing Author" }).ToList());

        Assert.AreEqual(20, resolved.Count, "only names that exist resolve; unknown names are simply absent");
        foreach (var person in existing)
        {
            Assert.AreEqual(person.Id, resolved[person.Name].Id, "each name resolves to its one unique Person row");
        }
        Assert.IsFalse(resolved.ContainsKey("Author 99"));
        Assert.IsFalse(resolved.ContainsKey("Missing Author"));
    }

    [TestMethod]
    public async Task GetByNamesAsync_EmptyNameSet_ReturnsAnEmptyMap()
    {
        var resolved = await _repository.GetByNamesAsync(new List<string>());

        Assert.AreEqual(0, resolved.Count, "no names in, no rows out - and no query against an empty IN clause");
    }

    // The batched lookup is chunked at the codebase's shared in-clause size, so a pathological
    // name set never becomes one over-limit IN (...) query - and every name still resolves.
    [TestMethod]
    public async Task GetByNamesAsync_MoreNamesThanTheInClauseChunkSize_ResolvesEveryOne()
    {
        var nameCount = ExpectedBookRepository.MaxInClauseIdsPerQuery * 2 + 20;
        var existing = Enumerable.Range(0, nameCount)
            .Select(i => new Person(default, $"Author {i:D4}"))
            .ToList();
        _db.Persons.AddRange(existing);
        await _db.SaveChangesAsync();

        var resolved = await _repository.GetByNamesAsync(
            existing.Select(p => p.Name).Concat(new[] { "No Such Author" }).ToList());

        Assert.AreEqual(nameCount, resolved.Count, "every stored name resolves across the chunk boundary, the unknown one stays absent");
        foreach (var person in existing)
        {
            Assert.AreEqual(person.Id, resolved[person.Name].Id);
        }
    }
}
