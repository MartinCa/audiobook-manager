using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class AuthorFollowRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AuthorFollowRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"authorfollowrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AuthorFollowRepository(_db);
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

    private async Task<long> SeedPersonAsync(string name, string? hardcoverAuthorId = null)
    {
        var person = new Person(default, name) { HardcoverAuthorId = hardcoverAuthorId };
        _db.Persons.Add(person);
        await _db.SaveChangesAsync();
        return person.Id;
    }

    [TestMethod]
    public async Task FollowAsync_CreatesAFollowRow()
    {
        var personId = await SeedPersonAsync("Brandon Sanderson");

        await _repository.FollowAsync(personId);

        Assert.IsTrue(await _repository.IsFollowedAsync(personId));
        Assert.AreEqual(1, await _db.AuthorFollows.CountAsync());
    }

    [TestMethod]
    public async Task FollowAsync_AlreadyFollowed_IsANoOp()
    {
        var personId = await SeedPersonAsync("Brandon Sanderson");

        await _repository.FollowAsync(personId);
        await _repository.FollowAsync(personId);

        Assert.AreEqual(1, await _db.AuthorFollows.CountAsync());
    }

    [TestMethod]
    public async Task UnfollowAsync_RemovesTheFollowRow()
    {
        var personId = await SeedPersonAsync("Brandon Sanderson");
        await _repository.FollowAsync(personId);

        await _repository.UnfollowAsync(personId);

        Assert.IsFalse(await _repository.IsFollowedAsync(personId));
    }

    [TestMethod]
    public async Task UnfollowAsync_NotFollowed_IsANoOp()
    {
        var personId = await SeedPersonAsync("Brandon Sanderson");

        await _repository.UnfollowAsync(personId);

        Assert.IsFalse(await _repository.IsFollowedAsync(personId));
    }

    [TestMethod]
    public async Task GetFollowedMatchedAuthorsAsync_OnlyReturnsFollowedAuthorsWithAHardcoverMatch()
    {
        var matchedFollowed = await SeedPersonAsync("Matched And Followed", hardcoverAuthorId: "123");
        var unmatchedFollowed = await SeedPersonAsync("Unmatched But Followed");
        var matchedUnfollowed = await SeedPersonAsync("Matched But Unfollowed", hardcoverAuthorId: "456");

        await _repository.FollowAsync(matchedFollowed);
        await _repository.FollowAsync(unmatchedFollowed);

        var result = await _repository.GetFollowedMatchedAuthorsAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(matchedFollowed, result[0].Id);
    }

    // Regression guard for the read-then-insert race: FollowAsync checks existence, then inserts,
    // across an await on a request-scoped context - two concurrent follows of the same author (a
    // duplicate click, or two tabs) can both see the row as missing and both try to insert it.
    // person_id is uniquely indexed, so the loser must adopt rather than fail the whole request
    // with a raw "UNIQUE constraint failed" - see the same pattern in
    // GenreRepositoryTests.GetOrCreateGenres_ConcurrentFirstWrites_AllSucceedAndCreateOneRowPerName.
    [TestMethod]
    public async Task FollowAsync_ConcurrentFirstFollows_AllSucceedAndCreateOneRow()
    {
        var personId = await SeedPersonAsync("Brandon Sanderson");
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new AuthorFollowRepository(context);
                calls.Add(Task.Run(() => repository.FollowAsync(personId)));
            }

            await Task.WhenAll(calls);

            var stored = await _db.AuthorFollows.AsNoTracking().Where(f => f.PersonId == personId).ToListAsync();
            Assert.AreEqual(1, stored.Count, "Every concurrent first-follow must converge on a single row, not fail or duplicate.");
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }
}
