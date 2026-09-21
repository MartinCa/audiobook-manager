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
        int year = 2024,
        string? coverFilePath = null,
        string? www = null,
        int? durationInSeconds = 7200)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, seriesPart, year,
            null, null, null, null, null, null, www, coverFilePath, durationInSeconds,
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
    public async Task GetSeriesOwnedBooksPageAsync_OrdersNumericSeriesPartsByValueAndBlanksLast()
    {
        // Regression for the BINARY-collation bug: series parts ordered lexically rendered
        // "1, 17.5, 2, 24, 3". Numeric parts must order by value, and a blank part must still
        // land last - the sort key has to keep the blank-last tier the explicit query used to.
        var latePart = await SeedBookAsync("Book Five", "Mistborn", seriesPart: "17.5");
        var firstPart = await SeedBookAsync("Book One", "Mistborn", seriesPart: "1");
        var secondPart = await SeedBookAsync("Book Two", "Mistborn", seriesPart: "2");
        var thirdPart = await SeedBookAsync("Book Three", "Mistborn", seriesPart: "3");
        var fourthPart = await SeedBookAsync("Book Four", "Mistborn", seriesPart: "24");
        var noPart = await SeedBookAsync("No Part", "Mistborn", seriesPart: null);

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 0, take: 10);

        Assert.AreSequenceEqual(
            new List<long> { firstPart.Id, secondPart.Id, thirdPart.Id, latePart.Id, fourthPart.Id, noPart.Id },
            items.Select(r => r.Id).ToList(),
            "numeric parts order by value (1, 2, 3, 17.5, 24) and blank parts come last");
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_OrdersNonNumericSeriesPartsAfterEveryNumericBeforeBlanks()
    {
        var partOne = await SeedBookAsync("Book One", "Mistborn", seriesPart: "2");
        var partTwo = await SeedBookAsync("Book Two", "Mistborn", seriesPart: "10");
        var textPart = await SeedBookAsync("Book Interlude", "Mistborn", seriesPart: "Book 2");
        var blankPart = await SeedBookAsync("Blank Part", "Mistborn", seriesPart: "  ");

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 0, take: 10);

        Assert.AreSequenceEqual(
            new List<long> { partOne.Id, partTwo.Id, textPart.Id, blankPart.Id },
            items.Select(r => r.Id).ToList(),
            "a non-numeric part called \"Book 2\" trails every numeric part and still precedes a blank one");
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

    // The series detail's owned section renders the cover from this row - a NULL here means the
    // section can never show existing cover art. The projection must carry it exactly as stored.
    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_ProjectsTheCoverFilePath()
    {
        const string coverPath = "/library/Brandon Sanderson/Mistborn/2006 - The Final Empire/cover.jpg";
        await SeedBookAsync("The Final Empire", "Mistborn", coverFilePath: coverPath);

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 0, take: 10);

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(coverPath, items.Single().CoverFilePath);
    }

    // The reconciliation's input is deliberately minimal: series part, book name and the row id.
    // The fuzzy matcher reads the first two; the id is what lets a part-mismatch be reported and
    // fixed against a specific owned book, so it has to be carried by the key too. The cache-refill
    // (the one read that touches every owned book of the series) still carries nothing else.
    [TestMethod]
    public async Task GetSeriesOwnedKeysAsync_ReturnsPartBookNameAndAudiobookIdForTheSeries()
    {
        var bookOne = await SeedBookAsync("Book One", "Mistborn", seriesPart: "1");
        var bookTwo = await SeedBookAsync("Book Two", "Mistborn", seriesPart: "2");
        await SeedBookAsync("Unrelated", "Stormlight Archive", seriesPart: "1");

        var (keys, overflow) = await _repository.GetSeriesOwnedKeysAsync("Mistborn", maxKeys: 100);

        Assert.IsFalse(overflow);
        Assert.AreEqual(2, keys.Count, "only the series' own books, not the whole library");
        Assert.IsTrue(keys.Any(k => k.SeriesPart == "1" && k.BookName == "Book One" && k.AudiobookId == bookOne.Id),
            "the projected id identifies the owned book it was read from");
        Assert.IsTrue(keys.Any(k => k.SeriesPart == "2" && k.BookName == "Book Two" && k.AudiobookId == bookTwo.Id),
            "the projected id identifies the owned book it was read from");
        Assert.IsFalse(keys.Any(k => k.BookName == "Unrelated"));
        Assert.IsTrue(keys.All(k => k is { AudiobookId: > 0, SeriesPart: not null, BookName.Length: > 0 }),
            "the key carries the series part, book name and audiobook id - and nothing else");
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

    // Bug 8 (unified owned-book list): the series detail's owned section gets the same text
    // search the whole-library book list offers, scoped to the series.
    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_Search_NarrowsToMatchingBooksOnly()
    {
        var match = await SeedBookAsync("The Final Empire", "Mistborn");
        await SeedBookAsync("The Well of Ascension", "Mistborn");

        var (items, total) = await _repository.GetSeriesOwnedBooksPageAsync(
            "Mistborn", skip: 0, take: 10, search: "final empire");

        Assert.AreEqual(1, total);
        Assert.AreEqual(match.Id, items.Single().Id);
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_Search_IsAccentInsensitive()
    {
        var match = await SeedBookAsync("René's Journey", "Mistborn");

        var (items, total) = await _repository.GetSeriesOwnedBooksPageAsync(
            "Mistborn", skip: 0, take: 10, search: "Rene");

        Assert.AreEqual(1, total);
        Assert.AreEqual(match.Id, items.Single().Id);
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_Filter_NarrowsByDurationRange()
    {
        var shortBook = await SeedBookAsync("Short Book", "Mistborn", durationInSeconds: 1800);
        await SeedBookAsync("Long Book", "Mistborn", durationInSeconds: 36000);

        var (items, total) = await _repository.GetSeriesOwnedBooksPageAsync(
            "Mistborn", skip: 0, take: 10,
            filter: new BookSummaryFilter(MaxDurationInSeconds: 3600));

        Assert.AreEqual(1, total);
        Assert.AreEqual(shortBook.Id, items.Single().Id);
    }

    [TestMethod]
    public async Task GetSeriesOwnedBooksPageAsync_ProjectsIsMatchedAndMatchedSourceName()
    {
        // MatchedSourceName is derived from Www by AccentFoldedColumnsInterceptor
        // (MetadataSourceResolution) on save - it is never set directly.
        await SeedBookAsync("Matched Book", "Mistborn", www: "https://hardcover.app/books/the-final-empire");
        await SeedBookAsync("Unmatched Book", "Mistborn");

        var (items, _) = await _repository.GetSeriesOwnedBooksPageAsync("Mistborn", skip: 0, take: 10);

        var matched = items.Single(r => r.BookName == "Matched Book");
        var unmatched = items.Single(r => r.BookName == "Unmatched Book");
        Assert.IsTrue(matched.IsMatched);
        Assert.AreEqual("Hardcover", matched.MatchedSourceName);
        Assert.IsFalse(unmatched.IsMatched);
        Assert.IsNull(unmatched.MatchedSourceName);
    }
}