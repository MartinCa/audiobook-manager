using AudiobookManager.Database;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class IgnoredSimilarValuePairRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private IgnoredSimilarValuePairRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ignoredpairrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new IgnoredSimilarValuePairRepository(_db);
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

    [TestMethod]
    public async Task AddRangeAsync_InsertsNewPairs()
    {
        await _repository.AddRangeAsync("authors", new[] { ("Ben Winters", "Ed Winters") });

        var pairs = await _repository.GetForKindAsync("authors");

        Assert.AreEqual(1, pairs.Count);
        Assert.AreEqual("Ben Winters", pairs[0].ValueA);
        Assert.AreEqual("Ed Winters", pairs[0].ValueB);
    }

    [TestMethod]
    public async Task AddRangeAsync_ExistingPair_IsIdempotentAndDoesNotDuplicate()
    {
        await _repository.AddRangeAsync("authors", new[] { ("Ben Winters", "Ed Winters") });
        await _repository.AddRangeAsync("authors", new[] { ("Ben Winters", "Ed Winters") });

        var pairs = await _repository.GetForKindAsync("authors");

        Assert.AreEqual(1, pairs.Count);
    }

    [TestMethod]
    public async Task AddRangeAsync_SameValuesDifferentKind_AreIndependent()
    {
        await _repository.AddRangeAsync("authors", new[] { ("A Value", "B Value") });
        await _repository.AddRangeAsync("series", new[] { ("A Value", "B Value") });

        Assert.AreEqual(1, (await _repository.GetForKindAsync("authors")).Count);
        Assert.AreEqual(1, (await _repository.GetForKindAsync("series")).Count);
    }

    [TestMethod]
    public async Task GetForKindAsync_UnknownKind_ReturnsEmpty()
    {
        var pairs = await _repository.GetForKindAsync("series");
        Assert.AreEqual(0, pairs.Count);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheRow()
    {
        await _repository.AddRangeAsync("authors", new[] { ("Ben Winters", "Ed Winters") });
        var id = (await _repository.GetForKindAsync("authors"))[0].Id;

        await _repository.DeleteAsync("authors", id);

        Assert.AreEqual(0, (await _repository.GetForKindAsync("authors")).Count);
    }

    [TestMethod]
    public async Task DeleteAsync_WrongKind_IsANoOp()
    {
        await _repository.AddRangeAsync("authors", new[] { ("Ben Winters", "Ed Winters") });
        var id = (await _repository.GetForKindAsync("authors"))[0].Id;

        await _repository.DeleteAsync("series", id);

        Assert.AreEqual(1, (await _repository.GetForKindAsync("authors")).Count);
    }

    [TestMethod]
    public async Task DeleteAsync_UnknownId_IsANoOp()
    {
        await _repository.DeleteAsync("authors", 12345);
        // No exception is the assertion.
    }

    // The unique index is the concurrency backstop: two callers racing to add the same pair must
    // both succeed and leave exactly one row, mirroring PersonRepository.GetOrCreatePersons.
    [TestMethod]
    public async Task AddRangeAsync_ConcurrentFirstWrites_AllSucceedAndCreateOneRow()
    {
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new IgnoredSimilarValuePairRepository(context);
                calls.Add(Task.Run(() => repository.AddRangeAsync("authors", new[] { ("Ben Winters", "Ed Winters") })));
            }

            await Task.WhenAll(calls);

            var stored = await _db.IgnoredSimilarValuePairs.AsNoTracking().ToListAsync();
            Assert.AreEqual(1, stored.Count);
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    // Regression: AddRangeAsync fans multiple pairs into one AddRange + SaveChanges, and SQLite
    // aborts that whole batch on a single unique violation. IgnorePairAsync calls this with one
    // pair per "against" value, so a batch containing several distinct pairs must not let a race
    // on just one of them silently drop the others - only the pair that actually lost the race
    // may go missing.
    [TestMethod]
    public async Task AddRangeAsync_BatchWithOneCollidingPair_StillInsertsTheOtherPairsInTheBatch()
    {
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        using var otherContext = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        var otherRepository = new IgnoredSimilarValuePairRepository(otherContext);

        var sharedPair = ("Ben Winters", "Ben Winter");
        var uniquePairs = new[]
        {
            ("Ben Winters", "Ed Winters"),
            ("Ben Winters", "Benjamin Winters"),
        };

        var batchCall = _repository.AddRangeAsync(
            "authors", uniquePairs.Append(sharedPair));
        var racingCall = otherRepository.AddRangeAsync("authors", new[] { sharedPair });

        await Task.WhenAll(batchCall, racingCall);

        var stored = await _db.IgnoredSimilarValuePairs.AsNoTracking().ToListAsync();

        // Exactly one row per distinct pair - the shared pair is not duplicated regardless of
        // which of the two calls won the race, and both of the batch's other pairs survive.
        Assert.AreEqual(3, stored.Count);
        foreach (var (valueA, valueB) in uniquePairs)
        {
            Assert.IsTrue(
                stored.Any(p => p.ValueA == valueA && p.ValueB == valueB),
                $"Expected pair ({valueA}, {valueB}) to survive the batch despite the race on a sibling pair.");
        }
    }
}
