using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class UpcomingReleaseRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private UpcomingReleaseRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"upcomingreleaserepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new UpcomingReleaseRepository(_db);
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

    private async Task<long> SeedPersonAsync(string name = "Brandon Sanderson")
    {
        var person = new Person(default, name);
        _db.Persons.Add(person);
        await _db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<long> SeedSeriesAsync(string name = "The Stormlight Archive")
    {
        var series = new Series { Name = name };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();
        return series.Id;
    }

    private static UpcomingRelease MakeRelease(
        string title = "Untitled Mistborn Novel",
        DateOnly? releaseDate = null,
        long? personId = null,
        long? seriesId = null,
        string? seriesPosition = null,
        string sourceBookId = "999",
        string? sourceUrl = null,
        string? imageUrl = null) => new()
    {
        Title = title,
        ReleaseDate = releaseDate ?? new DateOnly(2030, 1, 1),
        PersonId = personId,
        SeriesId = seriesId,
        SeriesPosition = seriesPosition,
        SourceName = "Hardcover",
        SourceBookId = sourceBookId,
        SourceUrl = sourceUrl,
        ImageUrl = imageUrl,
        DiscoveredAt = DateTime.UtcNow,
    };

    [TestMethod]
    public async Task UpsertAsync_NewRelease_Inserts()
    {
        var personId = await SeedPersonAsync();

        await _repository.UpsertAsync(MakeRelease(personId: personId));

        var stored = await _db.UpcomingReleases.AsNoTracking().SingleAsync();
        Assert.AreEqual("Untitled Mistborn Novel", stored.Title);
        Assert.AreEqual(personId, stored.PersonId);
    }

    // Regression guard: a source routinely ships an announced book under a placeholder title
    // ("Untitled Mistborn novel") and corrects it once real details are available - the stored
    // row must track that correction on the next poll, not freeze it at first discovery.
    [TestMethod]
    public async Task UpsertAsync_ExistingRelease_RefreshesTitleAndDateAndLinks()
    {
        await _repository.UpsertAsync(MakeRelease(
            title: "Untitled Mistborn Novel",
            releaseDate: new DateOnly(2030, 6, 1),
            sourceUrl: "https://hardcover.app/books/999-placeholder"));

        await _repository.UpsertAsync(MakeRelease(
            title: "The Lost Metal",
            releaseDate: new DateOnly(2030, 1, 15),
            sourceUrl: "https://hardcover.app/books/999-the-lost-metal",
            imageUrl: "https://hardcover.app/covers/999.jpg"));

        var stored = await _db.UpcomingReleases.AsNoTracking().SingleAsync();
        Assert.AreEqual("The Lost Metal", stored.Title);
        Assert.AreEqual(new DateOnly(2030, 1, 15), stored.ReleaseDate);
        Assert.AreEqual("https://hardcover.app/books/999-the-lost-metal", stored.SourceUrl);
        Assert.AreEqual("https://hardcover.app/covers/999.jpg", stored.ImageUrl);
    }

    [TestMethod]
    public async Task UpsertAsync_ExistingRelease_FillsInAPreviouslyMissingSeriesLinkWithoutClobberingThePersonLink()
    {
        var personId = await SeedPersonAsync();
        var seriesId = await SeedSeriesAsync();

        await _repository.UpsertAsync(MakeRelease(personId: personId, seriesId: null));

        await _repository.UpsertAsync(MakeRelease(personId: null, seriesId: seriesId, seriesPosition: "6"));

        var stored = await _db.UpcomingReleases.AsNoTracking().SingleAsync();
        Assert.AreEqual(personId, stored.PersonId);
        Assert.AreEqual(seriesId, stored.SeriesId);
        Assert.AreEqual("6", stored.SeriesPosition);
    }

    [TestMethod]
    public async Task UpsertAsync_ExistingRelease_NeverClearsAnAlreadySetLink()
    {
        var personId = await SeedPersonAsync();
        var seriesId = await SeedSeriesAsync();

        await _repository.UpsertAsync(MakeRelease(personId: personId, seriesId: seriesId, seriesPosition: "6"));

        // A later poll (of just the author, say) reports no series - the already-known links
        // must survive since a poll with a genuinely different series would be a different book.
        await _repository.UpsertAsync(MakeRelease(personId: null, seriesId: null, seriesPosition: null));

        var stored = await _db.UpcomingReleases.AsNoTracking().SingleAsync();
        Assert.AreEqual(personId, stored.PersonId);
        Assert.AreEqual(seriesId, stored.SeriesId);
        Assert.AreEqual("6", stored.SeriesPosition);
    }

    [TestMethod]
    public async Task UpsertAsync_NeverUpdatesDiscoveredAt()
    {
        await _repository.UpsertAsync(MakeRelease());
        var firstDiscoveredAt = (await _db.UpcomingReleases.AsNoTracking().SingleAsync()).DiscoveredAt;

        await Task.Delay(10);
        await _repository.UpsertAsync(MakeRelease(title: "Updated Title"));

        var stored = await _db.UpcomingReleases.AsNoTracking().SingleAsync();
        Assert.AreEqual(firstDiscoveredAt, stored.DiscoveredAt);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheRow()
    {
        await _repository.UpsertAsync(MakeRelease());
        var id = (await _db.UpcomingReleases.AsNoTracking().SingleAsync()).Id;

        var deleted = await _repository.DeleteAsync(id);

        Assert.IsTrue(deleted);
        Assert.AreEqual(0, await _db.UpcomingReleases.CountAsync());
    }

    [TestMethod]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        var deleted = await _repository.DeleteAsync(999);

        Assert.IsFalse(deleted);
    }

    // Regression guard for the read-then-insert race in UpsertAsync's winner-adoption path: a
    // book discovered through a followed author and a followed series it belongs to can be
    // polled at close to the same time, both racing to insert the first row for the same
    // (source_name, source_book_id). The unique index on that pair means one write wins and the
    // other must adopt and fill in rather than fail with a raw UNIQUE constraint violation - see
    // the identical pattern in GenreRepositoryTests and AuthorFollowRepositoryTests.
    [TestMethod]
    public async Task UpsertAsync_ConcurrentFirstDiscoveries_AllSucceedAndCreateOneRow()
    {
        var personId = await SeedPersonAsync();
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new UpcomingReleaseRepository(context);
                // Every concurrent caller reports the same book (the author-side poll's shape),
                // so the row that lands is identical regardless of which write wins the race.
                calls.Add(Task.Run(() => repository.UpsertAsync(MakeRelease(personId: personId))));
            }

            await Task.WhenAll(calls);

            var stored = await _db.UpcomingReleases.AsNoTracking().ToListAsync();
            Assert.AreEqual(1, stored.Count, "Every concurrent first-discovery must converge on a single row, not fail or duplicate.");
            Assert.AreEqual(personId, stored[0].PersonId);
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
