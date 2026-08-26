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

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_ThrowsWhenNothingMatches()
    {
        await SeedSeriesAsync();

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredAsync("Mistborn", "99", "Nonexistent", true));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredAsync("Unknown Series", "1", "Whatever", true));
    }
}
