using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the roster ignore path against a real (temp-file) SQLite database, because the
/// point of the natural-key addressing is exactly what happens to row ids across a roster
/// replace.
/// </summary>
[TestClass]
public class SeriesRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private SeriesRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new SeriesRepository(_db);
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

    private async Task<Series> SeedSeriesAsync()
    {
        var series = await _repository.UpsertSeriesAsync(new Series
        {
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
        });

        await _repository.ReplaceExpectedBooksAsync(series.Id, new List<SeriesExpectedBook>
        {
            new() { Title = "The Final Empire", Position = "1" },
            new() { Title = "The Well of Ascension", Position = "2" },
            new() { Title = "Secret History", Position = "3.5" },
        });

        return series;
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_FlagsTheEntryMatchingTheNaturalKey()
    {
        var series = await SeedSeriesAsync();

        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true);

        var stored = await _repository.GetByIdWithExpectedBooksAsync(series.Id);
        Assert.IsNotNull(stored);
        Assert.IsTrue(stored.ExpectedBooks.Single(b => b.Title == "Secret History").IsIgnored);
        Assert.IsFalse(stored.ExpectedBooks.Where(b => b.Title != "Secret History").Any(b => b.IsIgnored));
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_StillHitsTheSameLogicalBookAfterARosterReplace()
    {
        var series = await SeedSeriesAsync();
        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true);

        var idBeforeRefresh = (await _repository.GetByIdWithExpectedBooksAsync(series.Id))!
            .ExpectedBooks.Single(b => b.Title == "Secret History").Id;

        // A refresh replaces the whole roster - rows are deleted and re-inserted with new ids
        // (and in a different order), which is exactly what makes a cached id unsafe.
        await _repository.ReplaceExpectedBooksAsync(series.Id, new List<SeriesExpectedBook>
        {
            new() { Title = "Secret History", Position = "3.5", IsIgnored = true },
            new() { Title = "The Final Empire", Position = "1" },
            new() { Title = "The Well of Ascension", Position = "2" },
            new() { Title = "The Hero of Ages", Position = "3" },
        });

        var refreshed = (await _repository.GetByIdWithExpectedBooksAsync(series.Id))!;
        var secretHistoryAfter = refreshed.ExpectedBooks.Single(b => b.Title == "Secret History");
        Assert.AreNotEqual(idBeforeRefresh, secretHistoryAfter.Id, "the roster replace should have re-issued row ids");
        Assert.IsTrue(secretHistoryAfter.IsIgnored, "the ignore flag should survive the roster replace");

        // Unignoring by the natural key finds the current row, whatever its id is now.
        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", false);

        var afterUnignore = (await _repository.GetByIdWithExpectedBooksAsync(series.Id))!;
        Assert.IsFalse(afterUnignore.ExpectedBooks.Single(b => b.Title == "Secret History").IsIgnored);
        Assert.IsFalse(afterUnignore.ExpectedBooks.Any(b => b.IsIgnored));
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_FallsBackToTheTitleWhenTheEntryHasNoPosition()
    {
        var series = await _repository.UpsertSeriesAsync(new Series { Name = "Standalones" });
        await _repository.ReplaceExpectedBooksAsync(series.Id, new List<SeriesExpectedBook>
        {
            new() { Title = "A Book Without A Position" },
        });

        await _repository.SetExpectedBookIgnoredAsync("Standalones", null, "A Book Without A Position", true);

        var stored = await _repository.GetByIdWithExpectedBooksAsync(series.Id);
        Assert.IsTrue(stored!.ExpectedBooks.Single().IsIgnored);
    }

    // Regression: series.name is unique and the upsert reads before it inserts, across an await
    // on a request-scoped context. Two callers creating the same series' first catalog row - a
    // bulk auto-match running while the user matches or toggles omnibus editions on one of those
    // same series - both found it missing and both inserted, and the loser failed the request
    // with a raw "UNIQUE constraint failed: series.name" 500. Reproduced live against the running
    // API before the fix: two of four concurrent calls returned 500.
    [TestMethod]
    public async Task SetIncludeOmnibusEditionsAsync_ConcurrentFirstWrites_AllSucceedAndCreateOneRow()
    {
        const string seriesName = "Concurrently Created Series";
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                // A context per caller, as each request scope gets its own.
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new SeriesRepository(context);
                calls.Add(Task.Run(() => repository.SetIncludeOmnibusEditionsAsync(seriesName, true)));
            }

            await Task.WhenAll(calls);

            var rows = await _db.Series.AsNoTracking().Where(s => s.Name == seriesName).ToListAsync();
            Assert.AreEqual(1, rows.Count);
            Assert.IsTrue(rows[0].IncludeOmnibusEditions);
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_ThrowsWhenNothingMatches()
    {
        await SeedSeriesAsync();

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredAsync("Mistborn", "99", "Nonexistent", true));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredAsync("Unknown Series", "1", "Whatever", true));
    }

    [TestMethod]
    public async Task FindExpectedBookAsync_ResolvesByPositionAndTitle()
    {
        var series = await SeedSeriesAsync();

        var book = await _repository.FindExpectedBookAsync("Mistborn", "1", "The Final Empire");

        Assert.IsNotNull(book);
        Assert.AreEqual("The Final Empire", book.Title);
        Assert.AreEqual("1", book.Position);
    }

    [TestMethod]
    public async Task FindExpectedBookAsync_ResolvesByPositionAlone()
    {
        var series = await SeedSeriesAsync();

        var book = await _repository.FindExpectedBookAsync("Mistborn", "2", null);

        Assert.IsNotNull(book);
        Assert.AreEqual("The Well of Ascension", book.Title);
    }

    [TestMethod]
    public async Task FindExpectedBookAsync_ResolvesByTitleAlone()
    {
        var series = await SeedSeriesAsync();

        var book = await _repository.FindExpectedBookAsync("Mistborn", null, "Secret History");

        Assert.IsNotNull(book);
        Assert.AreEqual("3.5", book.Position);
    }

    [TestMethod]
    public async Task FindExpectedBookAsync_ReturnsNullWhenNothingMatches()
    {
        var series = await SeedSeriesAsync();

        var noEntry = await _repository.FindExpectedBookAsync("Mistborn", "9", "No Such Book");
        var noSeries = await _repository.FindExpectedBookAsync("Unknown Series", "1", "The Final Empire");

        Assert.IsNull(noEntry);
        Assert.IsNull(noSeries);
    }

    // The detail page loads the catalog metadata (matched-source fields, omnibus setting) on
    // every page request, but not the roster - the roster is only needed when the reconciliation
    // refills. GetByNameAsync must therefore be the metadata-only projection.
    [TestMethod]
    public async Task GetByNameAsync_ReturnsMetadataWithoutTheRoster()
    {
        var series = await SeedSeriesAsync();

        var row = await _repository.GetByNameAsync("Mistborn");

        Assert.IsNotNull(row);
        Assert.AreEqual("Hardcover", row!.MatchedSourceName);
        Assert.AreEqual("42", row.MatchedSourceId);
        Assert.AreEqual(0, row.ExpectedBooks.Count,
            "the roster must not be loaded for a metadata-only request");
        Assert.IsNull(await _repository.GetByNameAsync("Unknown Series"));
    }

    private async Task<(Series Series, List<SeriesExpectedBook> Books)> SeedRosterAsync(string name, int bookCount)
    {
        var series = await _repository.UpsertSeriesAsync(new Series { Name = name });
        var books = Enumerable.Range(1, bookCount)
            .Select(i => new SeriesExpectedBook { Title = $"Book {i:00}", Position = (i % 7).ToString() })
            .ToList();
        await _repository.ReplaceExpectedBooksAsync(series.Id, books);
        return (series, books);
    }

    // Regression for the materialization cap: a pathological stored roster (more than the
    // reconciliation's cap) must not be loaded whole. The query returns exactly cap+1 entries
    // and the Overflow flag, proving SQL limits the fetch before entity materialization.
    [TestMethod]
    public async Task GetByNameWithExpectedBooksBoundedAsync_OverTheCap_ReturnsOnlyCapPlusOneAndFlagsOverflow()
    {
        await SeedRosterAsync("Big Series", bookCount: 25);

        var (row, overflow) = await _repository.GetByNameWithExpectedBooksBoundedAsync("Big Series", maxExpectedBooks: 10);

        Assert.IsTrue(overflow, "a roster larger than the cap must be detected");
        Assert.IsNotNull(row);
        Assert.AreEqual(11, row!.ExpectedBooks.Count,
            "only cap+1 roster entries may be materialized - never the whole roster");
        Assert.AreEqual("Big Series", row.Name, "the catalog metadata still comes back");
    }

    [TestMethod]
    public async Task GetByNameWithExpectedBooksBoundedAsync_AtTheCap_ComesBackCompleteWithNoOverflow()
    {
        var (_, books) = await SeedRosterAsync("Small Series", bookCount: 5);

        var (row, overflow) = await _repository.GetByNameWithExpectedBooksBoundedAsync("Small Series", maxExpectedBooks: 10);

        Assert.IsFalse(overflow, "exactly-at-cap is a normal size");
        Assert.IsNotNull(row);
        Assert.AreEqual(5, row!.ExpectedBooks.Count);
        CollectionAssert.AreEquivalent(books.Select(b => b.Title).ToList(), row.ExpectedBooks.Select(b => b.Title).ToList());
    }

    [TestMethod]
    public async Task GetByNameWithExpectedBooksBoundedAsync_UnknownSeries_ReturnsNullAndNoOverflow()
    {
        var (row, overflow) = await _repository.GetByNameWithExpectedBooksBoundedAsync("Nonexistent", maxExpectedBooks: 10);

        Assert.IsNull(row);
        Assert.IsFalse(overflow);
    }
}
