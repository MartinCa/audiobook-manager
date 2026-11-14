using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The "multiple authors" and "has main author" book-list filters of <see cref="BookSummaryFilter"/>
/// against real SQLite: both are decided in SQL off the author join table and the main author column.
/// </summary>
[TestClass]
public class AudiobookRepositoryAuthorFilterTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"audiobookrepo-authorfilter-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>().Options, settings);
        _db.Database.EnsureCreated();
        _repository = new AudiobookRepository(_db);

        // Solo: one author, no main. Duo: two authors, no main. DuoMain: two authors with a main.
        // SoloMain: one author who is also marked main. None: no author at all.
        await SeedAsync("Solo", authors: ["A One"]);
        await SeedAsync("Duo", authors: ["A One", "B Two"]);
        await SeedAsync("DuoMain", authors: ["A One", "B Two"], mainIndex: 1);
        await SeedAsync("SoloMain", authors: ["C Three"], mainIndex: 0);
        await SeedAsync("None", authors: []);
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

    private async Task SeedAsync(string title, string[] authors, int? mainIndex = null)
    {
        var persons = new List<Person>();
        foreach (var name in authors)
        {
            var person = await _db.Persons.FirstOrDefaultAsync(p => p.Name == name) ?? new Person(default, name);
            if (person.Id == default)
            {
                _db.Persons.Add(person);
                await _db.SaveChangesAsync();
            }

            persons.Add(person);
        }

        _db.Audiobooks.Add(new Audiobook(
            default, title, null, null, null, 2024, null, null, null, null, null, null, null, null, null,
            $"/library/{title}.m4b", $"{title}.m4b", 1000)
        {
            Authors = persons,
            MainAuthorId = mainIndex is { } i ? persons[i].Id : null,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<string[]> TitlesAsync(BookSummaryFilter filter)
    {
        var (items, total) = await _repository.GetAllAsync(50, 0, filter);
        Assert.AreEqual(items.Count, total, "the page total must follow the filter");
        return items.Select(b => b.BookName).Order().ToArray();
    }

    [TestMethod]
    public async Task MultipleAuthorsTrue_KeepsOnlyBooksWithMoreThanOneAuthor()
    {
        CollectionAssert.AreEqual(
            new[] { "Duo", "DuoMain" }, await TitlesAsync(new BookSummaryFilter(MultipleAuthors: true)));
    }

    [TestMethod]
    public async Task MultipleAuthorsFalse_KeepsSingleAndNoAuthorBooks()
    {
        CollectionAssert.AreEqual(
            new[] { "None", "Solo", "SoloMain" }, await TitlesAsync(new BookSummaryFilter(MultipleAuthors: false)));
    }

    [TestMethod]
    public async Task HasMainAuthorTrue_KeepsOnlyBooksWithAMainAuthor()
    {
        CollectionAssert.AreEqual(
            new[] { "DuoMain", "SoloMain" }, await TitlesAsync(new BookSummaryFilter(HasMainAuthor: true)));
    }

    [TestMethod]
    public async Task HasMainAuthorFalse_KeepsOnlyBooksWithoutOne()
    {
        CollectionAssert.AreEqual(
            new[] { "Duo", "None", "Solo" }, await TitlesAsync(new BookSummaryFilter(HasMainAuthor: false)));
    }

    [TestMethod]
    public async Task BothFilters_AreAnded_SoYouCanFindCoAuthoredBooksStillMissingAMainAuthor()
    {
        CollectionAssert.AreEqual(
            new[] { "Duo" },
            await TitlesAsync(new BookSummaryFilter(MultipleAuthors: true, HasMainAuthor: false)));
    }

    [TestMethod]
    public async Task TheFiltersApplyToSearchToo()
    {
        var (items, total) = await _repository.SearchAsync(
            "Duo", 50, 0, filter: new BookSummaryFilter(HasMainAuthor: true));

        Assert.AreEqual(1, total);
        Assert.AreEqual("DuoMain", items.Single().BookName);
    }

    [TestMethod]
    public void EitherFilterAloneMakesTheFilterNonEmpty_ThoughFalseIsAChoiceToo()
    {
        Assert.IsFalse(new BookSummaryFilter(MultipleAuthors: true).IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(MultipleAuthors: false).IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(HasMainAuthor: true).IsEmpty);
        Assert.IsFalse(new BookSummaryFilter(HasMainAuthor: false).IsEmpty);
        Assert.IsTrue(new BookSummaryFilter().IsEmpty);
    }
}
