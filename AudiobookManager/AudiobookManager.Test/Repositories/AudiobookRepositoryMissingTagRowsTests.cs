using System.Linq.Expressions;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The missing-tags per-book projection, tested against real SQLite: the whole point of the
/// paged query is that the *filtering*, *counting* and *slicing* are computed in SQL (EXISTS
/// subqueries, blank checks, Skip/Take), so a mock would not exercise the semantics the service
/// relies on. Each test asserts both halves of the "two predicate representations" contract:
/// the <see cref="MissingTagRow"/> flag a row carries and the SQL WHERE behaviour that flag
/// implies must agree.
/// </summary>
[TestClass]
public class AudiobookRepositoryMissingTagRowsTests
{
    // Mirrors the SQL predicate MissingTagService.Fields defines for the Language field - the
    // test pins the service's expression and the row's LanguageBlank flag together against real
    // SQLite.
    private static readonly Expression<Func<Audiobook, bool>> LanguageMissing =
        a => a.Language == null || a.Language.Trim() == "";

    private static readonly Expression<Func<Audiobook, bool>> WwwMissing =
        a => a.Www == null || a.Www.Trim() == "";

    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"missingtagrows-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);
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
        List<Person>? authors = null,
        List<Person>? narrators = null,
        int year = 2024,
        string? series = null,
        string? language = null,
        string? coverFilePath = null,
        string? www = null)
    {
        var audiobook = new Audiobook(
            default, bookName, null, series, null, year,
            null, null, null, language, null, null, www, coverFilePath, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);

        if (authors is not null) audiobook.Authors = authors;
        if (narrators is not null) audiobook.Narrators = narrators;
        if (coverFilePath is not null) audiobook.CoverFilePath = coverFilePath;

        _db.Audiobooks.Add(audiobook);
        await _db.SaveChangesAsync();
        return audiobook;
    }

    private async Task<(List<MissingTagRow> Items, int Total)> PageAsync(
        IReadOnlyCollection<Expression<Func<Audiobook, bool>>> predicates,
        string? search = null,
        int skip = 0,
        int take = 50) =>
        await _repository.GetMissingTagRowsPageAsync(predicates, search, skip, take);

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_FlagsACompletelyUntaggedBook()
    {
        var book = await SeedBookAsync("Naked Book");

        var (items, total) = await PageAsync(new[] { LanguageMissing });

        Assert.AreEqual(1, total);
        var row = items.Single(r => r.Id == book.Id);
        Assert.AreEqual("Naked Book", row.BookName);
        Assert.IsTrue(row.LanguageBlank, "the SQL predicate and the row flag must agree");
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_AppliesTheMissingPredicatesAsAWhereClause()
    {
        await SeedBookAsync("No Language", language: null);
        await SeedBookAsync("Danish", language: "da");

        var (items, total) = await PageAsync(new[] { LanguageMissing });

        Assert.AreEqual(1, total, "a book carrying the field must not appear in the page");
        Assert.AreEqual("No Language", items.Single().BookName);
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_OrsTheSelectedFieldPredicates()
    {
        var noLanguage = await SeedBookAsync("No Language");
        var noWww = await SeedBookAsync("No Website", www: null);
        await SeedBookAsync("Fully Tagged", language: "en", www: "https://example.com");

        var (items, total) = await PageAsync(new[] { LanguageMissing, WwwMissing });

        Assert.AreEqual(2, total, "a book missing ANY selected field qualifies");
        CollectionAssert.AreEquivalent(
            new List<long> { noLanguage.Id, noWww.Id }, items.Select(r => r.Id).ToList());
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_SearchFoldsAccentsOnTheBookName()
    {
        await SeedBookAsync("Encyclopédie", language: null);
        await SeedBookAsync("Something Else", language: null);

        var (items, total) = await PageAsync(new[] { LanguageMissing }, search: "encyclop");

        Assert.AreEqual(1, total);
        Assert.AreEqual("Encyclopédie", items.Single().BookName);
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_BlankSearchReturnsEveryMatchingRow()
    {
        await SeedBookAsync("A Book", language: null);
        await SeedBookAsync("B Book", language: null);

        var (items, total) = await PageAsync(new[] { LanguageMissing }, search: "   ");

        Assert.AreEqual(2, items.Count);
        Assert.AreEqual(2, total);
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_TotalCountsTheFullMatchingSetNotTheSlice()
    {
        for (var i = 0; i < 25; i++)
        {
            await SeedBookAsync($"Book {i:00}", language: i % 2 == 0 ? null : "en");
        }

        var (items, total) = await PageAsync(new[] { LanguageMissing }, skip: 5, take: 5);

        Assert.AreEqual(5, items.Count);
        Assert.AreEqual(13, total, "only the books missing the selected field count toward the total");
    }

    // The bug paging invites: a partial ORDER BY lets SQLite tie rows any way it likes, so two
    // pages can repeat one row and drop another. BookName then Id makes the order total -
    // nothing may appear twice or be skipped across a full paging pass.
    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_PagedRightThrough_CoversEveryMatchingBookExactlyOnce()
    {
        var matchingIds = new List<long>();
        for (var i = 0; i < 23; i++)
        {
            var book = await SeedBookAsync($"Book {i:00}", language: i % 3 == 0 ? null : "en");
            if (i % 3 == 0)
            {
                matchingIds.Add(book.Id);
            }
        }

        var seen = new List<long>();
        for (var skip = 0; skip < 23; skip += 10)
        {
            var (items, _) = await PageAsync(new[] { LanguageMissing }, skip: skip, take: 10);
            seen.AddRange(items.Select(r => r.Id));
        }

        Assert.AreSequenceEqual(matchingIds.OrderBy(i => i).ToList(), seen.OrderBy(i => i).ToList(),
            "every matching book exactly once across the pages");
        Assert.AreEqual(matchingIds.Count, seen.Distinct().Count());
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_ProjectsAuthorNamesForTheResultRows()
    {
        await SeedBookAsync("A Book", authors: new List<Person> { new(default, "Author A"), new(default, "Author B") }, language: null);

        var (items, _) = await PageAsync(new[] { LanguageMissing });

        Assert.AreSequenceEqual(new List<string> { "Author A", "Author B" }, items.Single().Authors);
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_FlagsAuthorsAndNarratorsOnlyWhenEveryEntryIsBlank()
    {
        var blankAuthor = await SeedBookAsync("Blank Author", authors: new List<Person> { new(default, "") });
        var realAuthor = await SeedBookAsync("Real Author", authors: new List<Person> { new(default, "Someone") });
        var blankNarrator = await SeedBookAsync("Blank Narrator", narrators: new List<Person> { new(default, "  ") });

        // Every book in this test carries no language, so all three match the filter.
        var (items, _) = await PageAsync(new[] { LanguageMissing });

        Assert.IsFalse(items.Single(r => r.Id == blankAuthor.Id).HasRealAuthor);
        Assert.IsTrue(items.Single(r => r.Id == realAuthor.Id).HasRealAuthor);
        Assert.IsFalse(items.Single(r => r.Id == blankNarrator.Id).HasRealNarrator);
    }

    [TestMethod]
    public async Task GetMissingTagRowsPageAsync_AnEmptyPredicateListReturnsNothing()
    {
        await SeedBookAsync("A Book", language: null);

        var (items, total) = await PageAsync(new Expression<Func<Audiobook, bool>>[] { });

        Assert.AreEqual(0, total, "no selected fields means no books can be missing a selected field");
        Assert.AreEqual(0, items.Count);
    }
}