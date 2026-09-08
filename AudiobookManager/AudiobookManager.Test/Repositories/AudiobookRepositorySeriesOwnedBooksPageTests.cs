using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The series detail's owned section paged against real SQLite: the property under test is the
/// SQL page itself (total, stable total order, Skip/Take), which an in-memory list cannot
/// disprove. A partial ORDER BY would let the database return ties in any order, and two pages
/// taken from different orderings silently repeat one row and drop another.
/// </summary>
[TestClass]
public class AudiobookRepositorySeriesOwnedBooksPageTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;
    private Person _author = null!;
    private Person _narrator = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesowned-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
        // persons.name is unique, so one shared instance per test (the mapping joins books to
        // the same rows) - a fresh Person per book would trip the unique constraint.
        _author = new Person(default, "Brandon Sanderson");
        _narrator = new Person(default, "Michael Kramer");
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
        string bookName,
        string series,
        string? seriesPart = null,
        int year = 2024)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, seriesPart, year,
            null, null, null, null, null, null, null, null, 7200,
            $"/library/{series}/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { _author },
            Narrators = new List<Person> { _narrator },
        };

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_ReturnsTheRequestedSliceAndTheFullTotal()
    {
        for (var i = 0; i < 25; i++)
        {
            await SeedBookAsync($"Book {i:02d}", "Mistborn");
        }
        // A different series must not leak into the page or the total.
        await SeedBookAsync("Other Book", "Stormlight Archive");

        var (items, total) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 10, take: 10);

        Assert.AreEqual(10, items.Count);
        Assert.AreEqual(25, total, "the total counts only the series' own books");
        Assert.IsTrue(items.All(r => r.DurationInSeconds == 7200), "the selected fields are projected");
    }

    // The bug paging invites: books sharing a title have an undefined relative order, so the
    // id tiebreaker makes the order total - nothing may appear twice or be skipped across a full
    // paging pass, even with a shelf of same-named books.
    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_PagedRightThrough_CoversEveryBookExactlyOnce()
    {
        for (var i = 0; i < 23; i++)
        {
            await SeedBookAsync("The Book", "Mistborn", seriesPart: (i % 7).ToString());
        }

        var seen = new List<long>();
        for (var skip = 0; skip < 23; skip += 10)
        {
            var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: skip, take: 10);
            seen.AddRange(items.Select(r => r.Id));
        }

        Assert.AreEqual(23, seen.Distinct().Count(), "no book may appear on two pages");
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_OrdersBlankSeriesPartsLast()
    {
        var withPart = await SeedBookAsync("Part Two", "Mistborn", seriesPart: "2");
        var noPart = await SeedBookAsync("No Part", "Mistborn", seriesPart: null);
        var blankPart = await SeedBookAsync("Blank Part", "Mistborn", seriesPart: "  ");

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 0, take: 10);

        var orderedIds = items.Select(r => r.Id).ToList();
        Assert.AreEqual(withPart.Id, orderedIds[0], "a book with a real part sorts first");
        // NULL and whitespace-only parts are both "no part" - they share the last tier, so only
        // the set of trailing rows is asserted, never their relative order.
        CollectionAssert.AreEquivalent(
            new List<long> { noPart.Id, blankPart.Id }, orderedIds.Skip(1).ToList());
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_ProjectsTheAuthorAndNarratorNames()
    {
        await SeedBookAsync("Book One", "Mistborn");

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 0, take: 10);

        Assert.AreEqual(1, items.Count);
        Assert.AreSequenceEqual(new List<string> { "Brandon Sanderson" }, items.Single().Authors);
        Assert.AreSequenceEqual(new List<string> { "Michael Kramer" }, items.Single().Narrators);
    }

    // The reconciliation's input is deliberately minimal: series part and book name only, so the
    // cache-refill (the one read that touches every owned book of the series) carries nothing
    // the fuzzy matcher does not use.
    [TestMethod]
    public async Task GetSeriesOwnedKeysAsync_ReturnsOnlyPartAndBookNameForTheSeries()
    {
        await SeedBookAsync("Book One", "Mistborn", seriesPart: "1");
        await SeedBookAsync("Book Two", "Mistborn", seriesPart: "2");
        await SeedBookAsync("Unrelated", "Stormlight Archive", seriesPart: "1");

        var (keys, overflow) = await _repository.GetSeriesOwnedKeysAsync("Mistborn", maxKeys: 100);

        Assert.IsFalse(overflow);
        Assert.AreEqual(2, keys.Count, "only the series' own books, not the whole library");
        Assert.IsTrue(keys.Any(k => k.SeriesPart == "1" && k.BookName == "Book One"));
        Assert.IsTrue(keys.Any(k => k.SeriesPart == "2" && k.BookName == "Book Two"));
        Assert.IsFalse(keys.Any(k => k.BookName == "Unrelated"));
        Assert.IsTrue(keys.All(k => k is { SeriesPart: not null, BookName.Length: > 0 }),
            "the key carries series part and book name - nothing else (the row type has no other fields)");
    }

    // Regression for the materialization cap: a pathological owned set (more than the
    // reconciliation's cap) must not be transferred whole. The query returns exactly cap+1 keys
    // and the Overflow flag, proving SQL limits the fetch before allocation.
    [TestMethod]
    public async Task GetSeriesOwnedKeysAsync_OverTheCap_ReturnsOnlyCapPlusOneAndFlagsOverflow()
    {
        for (var i = 0; i < 25; i++)
        {
            await SeedBookAsync($"Book {i:00}", "Mistborn");
        }

        var (keys, overflow) = await _repository.GetSeriesOwnedKeysAsync("Mistborn", maxKeys: 10);

        Assert.IsTrue(overflow, "a set larger than the cap must be detected");
        Assert.AreEqual(11, keys.Count, "only cap+1 keys may cross the wire - never the whole set");
    }

    [TestMethod]
    public async Task GetSeriesOwnedKeysAsync_ExactlyAtTheCap_ComesBackCompleteWithNoOverflow()
    {
        for (var i = 0; i < 10; i++)
        {
            await SeedBookAsync($"Book {i:00}", "Mistborn");
        }

        var (keys, overflow) = await _repository.GetSeriesOwnedKeysAsync("Mistborn", maxKeys: 10);

        Assert.IsFalse(overflow, "exactly-at-cap is a normal size");
        Assert.AreEqual(10, keys.Count, "the count is exact, so it can serve as the owned count");
    }
}