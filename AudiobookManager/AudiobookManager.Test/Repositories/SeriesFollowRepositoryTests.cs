using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class SeriesFollowRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private SeriesFollowRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesfollowrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new SeriesFollowRepository(_db);
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

    private async Task<long> SeedSeriesAsync(string name, string? matchedSourceName = null, string? matchedSourceId = null)
    {
        var series = new Series { Name = name, MatchedSourceName = matchedSourceName, MatchedSourceId = matchedSourceId };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();
        return series.Id;
    }

    [TestMethod]
    public async Task FollowAsync_CreatesAFollowRow()
    {
        var seriesId = await SeedSeriesAsync("Mistborn");

        await _repository.FollowAsync(seriesId);

        Assert.IsTrue(await _repository.IsFollowedAsync(seriesId));
        Assert.AreEqual(1, await _db.SeriesFollows.CountAsync());
    }

    [TestMethod]
    public async Task FollowAsync_AlreadyFollowed_IsANoOp()
    {
        var seriesId = await SeedSeriesAsync("Mistborn");

        await _repository.FollowAsync(seriesId);
        await _repository.FollowAsync(seriesId);

        Assert.AreEqual(1, await _db.SeriesFollows.CountAsync());
    }

    [TestMethod]
    public async Task UnfollowAsync_RemovesTheFollowRow()
    {
        var seriesId = await SeedSeriesAsync("Mistborn");
        await _repository.FollowAsync(seriesId);

        await _repository.UnfollowAsync(seriesId);

        Assert.IsFalse(await _repository.IsFollowedAsync(seriesId));
    }

    [TestMethod]
    public async Task GetFollowedMatchedSeriesAsync_OnlyReturnsFollowedSeriesThatAreMatched()
    {
        var matchedFollowed = await SeedSeriesAsync("Mistborn", "Hardcover", "42");
        var unmatchedFollowed = await SeedSeriesAsync("The Stormlight Archive");
        // Matched but never followed - proves the query requires both, not either alone.
        await SeedSeriesAsync("Wheel of Time", "Hardcover", "7");

        await _repository.FollowAsync(matchedFollowed);
        await _repository.FollowAsync(unmatchedFollowed);

        var result = await _repository.GetFollowedMatchedSeriesAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(matchedFollowed, result[0].Id);
    }

    // Same read-then-insert race as AuthorFollowRepositoryTests - series_id is uniquely indexed,
    // so two concurrent first-follows must converge on one row rather than one of them raising a
    // raw UNIQUE constraint failure.
    [TestMethod]
    public async Task FollowAsync_ConcurrentFirstFollows_AllSucceedAndCreateOneRow()
    {
        var seriesId = await SeedSeriesAsync("Mistborn");
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new SeriesFollowRepository(context);
                calls.Add(Task.Run(() => repository.FollowAsync(seriesId)));
            }

            await Task.WhenAll(calls);

            var stored = await _db.SeriesFollows.AsNoTracking().Where(f => f.SeriesId == seriesId).ToListAsync();
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
