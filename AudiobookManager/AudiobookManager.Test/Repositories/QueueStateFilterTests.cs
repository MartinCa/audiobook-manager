using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The "Queue status" filter (QueueState) on the book, series and author lists, against real
/// SQLite: the property under test is how the filter reads the review-queue tables, which a
/// mocked repository cannot disprove. Selecting several states is a union; NotQueued is "in none
/// of the queues"; and - the reason the filter exists - NotQueued combined with the Unsupported
/// source leaves out every book a bulk online search already touched.
/// </summary>
[TestClass]
public class QueueStateFilterTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _books = null!;
    private PersonRepository _persons = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"queuestate-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _books = new AudiobookRepository(_db);
        _persons = new PersonRepository(_db);
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

    private async Task<Audiobook> SeedBookAsync(
        string bookName, string? series = null, string? authorName = null, string? www = null)
    {
        var book = new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, www, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, authorName ?? $"Author of {bookName}") }
        };
        _db.Audiobooks.Add(series is null ? book : book.WithPrimaryRelation());
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task AddMatchAsync(long audiobookId, PendingOnlineMatchStatus status)
    {
        _db.PendingOnlineMatches.Add(new PendingOnlineMatch
        {
            AudiobookId = audiobookId,
            SearchedAt = DateTime.UtcNow,
            Status = status,
            SourceNamesJson = "[]",
            ResultsJson = "[]",
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddPendingRefreshAsync(long audiobookId)
    {
        _db.PendingMetadataRefreshes.Add(new PendingMetadataRefresh
        {
            AudiobookId = audiobookId,
            FetchedAt = DateTime.UtcNow,
            SourceName = "Audible",
            SourceUrl = "https://www.audible.com/pd/1",
            PayloadJson = "{}",
        });
        await _db.SaveChangesAsync();
    }

    private async Task<List<string>> BookNamesAsync(params string[] states)
    {
        var (items, total) = await _books.GetAllAsync(50, 0, new BookSummaryFilter(QueueStates: states));
        Assert.AreEqual(items.Count, total, "the page and the total must agree");
        return items.Select(b => b.BookName).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private async Task SeedBookQueuesAsync()
    {
        await SeedBookAsync("Untouched");
        var pending = await SeedBookAsync("PendingMatch");
        await AddMatchAsync(pending.Id, PendingOnlineMatchStatus.Pending);
        var rejected = await SeedBookAsync("RejectedMatch");
        await AddMatchAsync(rejected.Id, PendingOnlineMatchStatus.Rejected);
        var refresh = await SeedBookAsync("PendingRefresh");
        await AddPendingRefreshAsync(refresh.Id);
    }

    [TestMethod]
    public void IsEmpty_NoQueueStates_StaysEmptySoTheNormalViewIsNeverFiltered()
    {
        Assert.IsTrue(new BookSummaryFilter().IsEmpty);
        Assert.IsTrue(new BookSummaryFilter(QueueStates: Array.Empty<string>()).IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(QueueStates: new[] { QueueState.NotQueued }).IsEmpty);
        Assert.IsTrue(new SeriesOverviewFilter().IsEmpty);
        Assert.IsFalse(new SeriesOverviewFilter(QueueStates: new[] { QueueState.NotQueued }).IsEmpty);
        Assert.IsTrue(new AuthorSummaryFilter().IsEmpty);
        Assert.IsFalse(new AuthorSummaryFilter(QueueStates: new[] { QueueState.NotQueued }).IsEmpty);
    }

    [TestMethod]
    public async Task Books_NoQueueFilter_ListsEveryBookWhetherOrNotItIsQueued()
    {
        await SeedBookQueuesAsync();

        var (items, total) = await _books.GetAllAsync(50, 0, null);

        Assert.AreEqual(4, total);
        Assert.AreEqual(4, items.Count);
    }

    [TestMethod]
    public async Task Books_NotQueued_ExcludesPendingRejectedAndPendingRefreshBooks()
    {
        await SeedBookQueuesAsync();

        CollectionAssert.AreEqual(new[] { "Untouched" }, await BookNamesAsync(QueueState.NotQueued));
    }

    [TestMethod]
    public async Task Books_EachQueueState_ReturnsOnlyItsOwnBooks()
    {
        await SeedBookQueuesAsync();

        CollectionAssert.AreEqual(new[] { "PendingMatch" }, await BookNamesAsync(QueueState.MatchPending));
        CollectionAssert.AreEqual(new[] { "RejectedMatch" }, await BookNamesAsync(QueueState.MatchRejected));
        CollectionAssert.AreEqual(new[] { "PendingRefresh" }, await BookNamesAsync(QueueState.RefreshPending));
    }

    [TestMethod]
    public async Task Books_SeveralStates_AreAUnion_SoRejectedBooksCanBeRetried()
    {
        await SeedBookQueuesAsync();

        CollectionAssert.AreEqual(
            new[] { "RejectedMatch", "Untouched" },
            await BookNamesAsync(QueueState.NotQueued, QueueState.MatchRejected));
    }

    [TestMethod]
    public async Task Books_InSeveralQueuesAtOnce_AppearOnceAndAreNotNotQueued()
    {
        var book = await SeedBookAsync("Both");
        await AddMatchAsync(book.Id, PendingOnlineMatchStatus.Rejected);
        await AddPendingRefreshAsync(book.Id);

        CollectionAssert.AreEqual(new[] { "Both" }, await BookNamesAsync(QueueState.MatchRejected, QueueState.RefreshPending));
        CollectionAssert.AreEqual(Array.Empty<string>(), await BookNamesAsync(QueueState.NotQueued));
    }

    [TestMethod]
    public async Task Books_UnsupportedSourceAndNotQueued_IsTheWorkableBacklogOfBooksWithNoOnlineSource()
    {
        // The use case this filter was added for: the books with no online source that no bulk
        // search has touched yet. A book that has a source, and one a search already queued, are
        // both out.
        await SeedBookAsync("Backlog");
        await SeedBookAsync("HasSource", www: "https://www.audible.com/pd/12345");
        var searched = await SeedBookAsync("AlreadySearched");
        await AddMatchAsync(searched.Id, PendingOnlineMatchStatus.Pending);
        var rejected = await SeedBookAsync("Rejected");
        await AddMatchAsync(rejected.Id, PendingOnlineMatchStatus.Rejected);

        var (items, total) = await _books.GetAllAsync(50, 0, new BookSummaryFilter(
            Sources: new[] { BookSummaryFilter.UnsupportedSource },
            QueueStates: new[] { QueueState.NotQueued }));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Backlog", items.Single().BookName);
    }

    [TestMethod]
    public async Task Books_UnknownQueueStateOnly_MatchesNothingRatherThanEverything()
    {
        await SeedBookQueuesAsync();

        CollectionAssert.AreEqual(Array.Empty<string>(), await BookNamesAsync("NoSuchState"));
    }

    [TestMethod]
    public async Task Books_SearchAppliesTheQueueFilterToo()
    {
        await SeedBookAsync("Dune One");
        var queued = await SeedBookAsync("Dune Two");
        await AddMatchAsync(queued.Id, PendingOnlineMatchStatus.Pending);

        var (items, total) = await _books.SearchAsync(
            "dune", 20, 0, filter: new BookSummaryFilter(QueueStates: new[] { QueueState.NotQueued }));

        Assert.AreEqual(1, total);
        Assert.AreEqual("Dune One", items.Single().BookName);
    }

    [TestMethod]
    public async Task Series_QueueStates_SeparateRefreshPendingFailedAndUntouched_IncludingCatalogOnlyRows()
    {
        await SeedBookAsync("a1", series: "Pending Series");
        await SeedBookAsync("b1", series: "Failed Series");
        await SeedBookAsync("c1", series: "Plain Series");
        await SeedBookAsync("d1", series: "Also Plain");

        _db.PendingSeriesRefreshes.Add(new PendingSeriesRefresh
        {
            SeriesName = "Pending Series",
            FetchedAt = DateTime.UtcNow,
            SourceName = "Hardcover",
            SourceUrl = "https://hardcover.app/series/1",
            PayloadJson = "{}",
        });
        var failed = new Series { Name = "Failed Series" };
        var catalogOnlyFailed = new Series { Name = "Catalog Only Failed" };
        _db.Series.AddRange(failed, catalogOnlyFailed);
        await _db.SaveChangesAsync();
        _db.SeriesConsistencyIssues.AddRange(
            new SeriesConsistencyIssue { SeriesId = failed.Id, ErrorMessage = "boom", DetectedAt = DateTime.UtcNow },
            new SeriesConsistencyIssue { SeriesId = catalogOnlyFailed.Id, ErrorMessage = "boom", DetectedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        async Task<List<string>> NamesAsync(params string[] states)
        {
            var (names, total) = await _books.GetSeriesValuesPageAsync(
                null, null, skip: 0, take: 50, filter: new SeriesOverviewFilter(QueueStates: states));
            Assert.AreEqual(names.Count, total);
            return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        CollectionAssert.AreEqual(new[] { "Pending Series" }, await NamesAsync(QueueState.RefreshPending));
        CollectionAssert.AreEqual(new[] { "Catalog Only Failed", "Failed Series" }, await NamesAsync(QueueState.RefreshFailed));
        CollectionAssert.AreEqual(new[] { "Also Plain", "Plain Series" }, await NamesAsync(QueueState.NotQueued));
        CollectionAssert.AreEqual(
            new[] { "Also Plain", "Pending Series", "Plain Series" },
            await NamesAsync(QueueState.NotQueued, QueueState.RefreshPending));

        // And with no queue filter nothing is left out.
        var (all, allTotal) = await _books.GetSeriesValuesPageAsync(null, null, skip: 0, take: 50);
        Assert.AreEqual(5, allTotal);
        Assert.AreEqual(5, all.Count);
    }

    [TestMethod]
    public async Task Authors_QueueStates_SeparateRenamePendingFailedAndUntouched()
    {
        var renamePending = (await SeedBookAsync("r", authorName: "Rename Pending")).Authors.Single();
        var failed = (await SeedBookAsync("f", authorName: "Refresh Failed")).Authors.Single();
        await SeedBookAsync("p", authorName: "Plain Author");

        _db.PendingAuthorRefreshes.Add(new PendingAuthorRefresh
        {
            PersonId = renamePending.Id,
            ProposedName = "Rename Proposed",
            SourceName = "Hardcover",
            FetchedAt = DateTime.UtcNow,
        });
        _db.AuthorConsistencyIssues.Add(new AuthorConsistencyIssue
        {
            PersonId = failed.Id,
            ErrorMessage = "boom",
            DetectedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        async Task<List<string>> NamesAsync(params string[] states)
        {
            var (rows, total) = await _persons.GetAuthorSummariesPagedAsync(
                null, 50, 0, new AuthorSummaryFilter(QueueStates: states));
            Assert.AreEqual(rows.Count, total);
            return rows.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        CollectionAssert.AreEqual(new[] { "Rename Pending" }, await NamesAsync(QueueState.RenamePending));
        CollectionAssert.AreEqual(new[] { "Refresh Failed" }, await NamesAsync(QueueState.RefreshFailed));
        CollectionAssert.AreEqual(new[] { "Plain Author" }, await NamesAsync(QueueState.NotQueued));
        CollectionAssert.AreEqual(
            new[] { "Plain Author", "Refresh Failed" },
            await NamesAsync(QueueState.NotQueued, QueueState.RefreshFailed));

        var (everyone, everyoneTotal) = await _persons.GetAuthorSummariesPagedAsync(null, 50, 0);
        Assert.AreEqual(3, everyoneTotal);
        Assert.AreEqual(3, everyone.Count);
    }
}
