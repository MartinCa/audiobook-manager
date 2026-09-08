using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class PersonRepositorySearchTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PersonRepository _repository = null!;
    private AudiobookRepository _audiobookRepository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"personrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PersonRepository(_db);
        _audiobookRepository = new AudiobookRepository(_db);
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

    // Regression test: GetOrCreatePersons reads the existing names then inserts the missing ones
    // across an await, and persons.name is unique - so two organizes running concurrently (the
    // bulk import fans out; OrganizeWorker runs alongside an interactive save) can both see a
    // new author as missing and both insert it. The loser used to die on the UNIQUE constraint,
    // failing the whole organize with the file half-processed. It must adopt the winner's row
    // instead. Fails against the pre-fix repository with a DbUpdateException.
    [TestMethod]
    public async Task GetOrCreatePersons_AnotherWriterInsertsTheSameNameFirst_AdoptsTheExistingRow()
    {
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });

        // Simulate the concurrent writer committing in the window between this repository's
        // read and its insert, by racing it in at exactly that point.
        void InsertConcurrently(object? sender, SavingChangesEventArgs args)
        {
            _db.SavingChanges -= InsertConcurrently;   // only race the first save
            using var otherContext = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
            otherContext.Persons.Add(new Person(default, "Adrian Tchaikovsky"));
            otherContext.SaveChanges();
        }

        _db.SavingChanges += InsertConcurrently;
        try
        {
            var result = await _repository.GetOrCreatePersons(new[] { "Adrian Tchaikovsky" });

            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.ContainsKey("Adrian Tchaikovsky"));
            Assert.AreNotEqual(default, result["Adrian Tchaikovsky"].Id, "should have adopted the persisted row");
        }
        finally
        {
            _db.SavingChanges -= InsertConcurrently;
        }

        // And exactly one row exists - the race must not have produced a duplicate.
        using var verifyContext = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        Assert.AreEqual(1, verifyContext.Persons.Count(p => p.Name == "Adrian Tchaikovsky"));
    }

    // Regression test: this ordered by Name in SQL, i.e. under SQLite's BINARY collation, which
    // sorts by code point - "Zadie" before "alice", and every accented surname after "Z". The
    // list is unpaged, so nothing forced the sort into SQL. Fails against the pre-fix
    // repository, which returns Zadie first.
    [TestMethod]
    public async Task GetAllAuthorSummariesAsync_SortsForAReaderNotByCodePoint()
    {
        await SeedBookWithAuthorAsync("Book A", "Zadie Smith");
        await SeedBookWithAuthorAsync("Book B", "alice Walker");
        await SeedBookWithAuthorAsync("Book C", "Ólafur Arnalds");

        var results = await _repository.GetAllAuthorSummariesAsync();

        CollectionAssert.AreEqual(
            new[] { "alice Walker", "Ólafur Arnalds", "Zadie Smith" },
            results.Select(r => r.Name).ToList());
    }

    // Same rank-before-the-limit bug as the book/series searches: the controller ranked the
    // survivors of an alphabetical Take, so a prefix match that sorted late was already gone.
    [TestMethod]
    public async Task SearchAuthorSummariesAsync_RanksPrefixMatchesBeforeApplyingTheLimit()
    {
        // All contain "san"; only "sandra Newman" starts with it, and its lowercase initial
        // sorts after every uppercase name under BINARY collation.
        foreach (var name in new[] { "Alec Sanders", "Bo Sanchez", "Cy Sanford", "Di Sansom", "Ed Santos" })
        {
            await SeedBookWithAuthorAsync($"Book by {name}", name);
        }
        await SeedBookWithAuthorAsync("Book by sandra", "sandra Newman");

        var (results, _) = await _repository.SearchAuthorSummariesAsync("san", 5, 0);

        Assert.AreEqual(5, results.Count);
        Assert.AreEqual("sandra Newman", results[0].Name);
    }

    private async Task SeedBookWithAuthorAsync(string bookName, string authorName) =>
        await SeedBookWithAuthorAsync(bookName, new Person(default, authorName));

    private async Task SeedBookWithAuthorAsync(string bookName, Person author)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { author }
        };

        await _audiobookRepository.InsertAudiobook(audiobook);
    }

    private async Task SeedBookWithNarratorAsync(string bookName, string narratorName)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, $"Placeholder Author for {bookName}") },
            Narrators = new List<Person> { new Person(default, narratorName) }
        };

        await _audiobookRepository.InsertAudiobook(audiobook);
    }

    [TestMethod]
    public async Task GetNarratorNamesAsync_OrdersForAReaderNotByCodePoint()
    {
        await SeedBookWithNarratorAsync("Book A", "alice munro");
        await SeedBookWithNarratorAsync("Book B", "Zadie Smith");
        await SeedBookWithNarratorAsync("Book C", "Avila Narrator");

        var names = await _repository.GetNarratorNamesAsync();

        CollectionAssert.AreEqual(
            new List<string> { "alice munro", "Avila Narrator", "Zadie Smith" },
            names);
    }

    [TestMethod]
    public async Task GetNarratorNamesAsync_ExcludesPeopleWithNoNarratedBooks()
    {
        await SeedBookWithNarratorAsync("Book A", "Narrating Narrator");
        await _repository.GetOrCreatePerson("Author Only");

        var names = await _repository.GetNarratorNamesAsync();

        CollectionAssert.AreEqual(new List<string> { "Narrating Narrator" }, names);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_ReturnsMatchingAuthorsWithBookCount()
    {
        await SeedBookWithAuthorAsync("Mistborn", "Brandon Sanderson");
        await SeedBookWithAuthorAsync("Dune", "Frank Herbert");

        var (results, total) = await _repository.SearchAuthorSummariesAsync("sander", 10, 0);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Brandon Sanderson", results[0].Name);
        Assert.AreEqual(1, results[0].BookCount);
        Assert.AreEqual(1, total);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_ExcludesAuthorsWithNoBooks()
    {
        await _repository.GetOrCreatePerson("Orphan Author");

        var (results, total) = await _repository.SearchAuthorSummariesAsync("orphan", 10, 0);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, total);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_ReturnsEmptyWhenNoAuthorMatches()
    {
        await SeedBookWithAuthorAsync("Dune", "Frank Herbert");

        var (results, total) = await _repository.SearchAuthorSummariesAsync("nonexistent", 10, 0);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, total);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_UnaccentedQueryMatchesAccentedAuthorName()
    {
        // SQLite's default BINARY collation (which LIKE uses) never folds diacritics, so typing
        // "rene" for "René" would otherwise return nothing - unfriendly for a name search.
        await SeedBookWithAuthorAsync("Le Petit Prince", "Antoine de Saint-Exupéry");

        var (results, _) = await _repository.SearchAuthorSummariesAsync("exupery", 10, 0);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Antoine de Saint-Exupéry", results[0].Name);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_TotalCountsAllMatchesRegardlessOfLimit()
    {
        foreach (var name in new[] { "Alec Sanders", "Bo Sanchez", "Cy Sanford", "Di Sansom", "Ed Santos" })
        {
            await SeedBookWithAuthorAsync($"Book by {name}", name);
        }

        var (results, total) = await _repository.SearchAuthorSummariesAsync("san", 2, 0);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual(5, total);
    }

    // Offset paging must return the deterministic next slice - ThenBy(Id) after ThenBy(Name)
    // gives the ordering a total order, so a fixed alphabetical set pages exactly.
    [TestMethod]
    public async Task SearchAuthorSummariesAsync_OffsetPaging_ReturnsTheDeterministicNextSlice()
    {
        foreach (var name in new[] { "Alec Sanders", "Bo Sanchez", "Cy Sanford", "Di Sansom", "Ed Santos" })
        {
            await SeedBookWithAuthorAsync($"Book by {name}", name);
        }

        var (page1, total1) = await _repository.SearchAuthorSummariesAsync("san", 2, 0);
        var (page2, total2) = await _repository.SearchAuthorSummariesAsync("san", 2, 2);
        var (page3, total3) = await _repository.SearchAuthorSummariesAsync("san", 2, 4);

        Assert.AreEqual(5, total1);
        Assert.AreEqual(5, total2);
        Assert.AreEqual(5, total3);
        CollectionAssert.AreEqual(
            new[] { "Alec Sanders", "Bo Sanchez" }, page1.Select(r => r.Name).ToList());
        CollectionAssert.AreEqual(
            new[] { "Cy Sanford", "Di Sansom" }, page2.Select(r => r.Name).ToList());
        CollectionAssert.AreEqual(
            new[] { "Ed Santos" }, page3.Select(r => r.Name).ToList());
    }

    // Regression coverage for paging combined with accent folding: the offset must apply after
    // the accent-folded LIKE filter, not before it.
    [TestMethod]
    public async Task SearchAuthorSummariesAsync_AccentFoldedQueryStillMatchesWhenPaged()
    {
        await SeedBookWithAuthorAsync("Le Petit Prince", "Antoine de Saint-Exupéry");
        await SeedBookWithAuthorAsync("Book B", "Another Exupery Person");

        var (results, total) = await _repository.SearchAuthorSummariesAsync("exupery", 1, 1);

        Assert.AreEqual(2, total);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Antoine de Saint-Exupéry", results[0].Name);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_AllNamesNew_CreatesEveryOneInASingleBatch()
    {
        var result = await _repository.GetOrCreatePersons(new[] { "Brandon Sanderson", "Frank Herbert" });

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result["Brandon Sanderson"].Id != default);
        Assert.IsTrue(result["Frank Herbert"].Id != default);
        Assert.AreNotEqual(result["Brandon Sanderson"].Id, result["Frank Herbert"].Id);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_AllNamesExisting_ReusesExistingRowsWithoutDuplicating()
    {
        var existing = await _repository.GetOrCreatePerson("Brandon Sanderson");

        var result = await _repository.GetOrCreatePersons(new[] { "Brandon Sanderson" });

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(existing.Id, result["Brandon Sanderson"].Id);

        var all = await _db.Persons.Where(p => p.Name == "Brandon Sanderson").ToListAsync();
        Assert.AreEqual(1, all.Count);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_MixOfExistingAndNewNames_ReusesExistingAndCreatesOnlyMissing()
    {
        var existing = await _repository.GetOrCreatePerson("Brandon Sanderson");

        var result = await _repository.GetOrCreatePersons(new[] { "Brandon Sanderson", "Frank Herbert" });

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(existing.Id, result["Brandon Sanderson"].Id);
        Assert.IsTrue(result["Frank Herbert"].Id != default);

        var allHerbert = await _db.Persons.Where(p => p.Name == "Frank Herbert").ToListAsync();
        Assert.AreEqual(1, allHerbert.Count);
    }

    [TestMethod]
    public async Task GetOrCreatePersons_EmptyInput_ReturnsEmptyDictionaryWithoutQuerying()
    {
        var result = await _repository.GetOrCreatePersons(Array.Empty<string>());

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetAuthorNamesAsync_OrdersForAReaderNotByCodePoint()
    {
        // Regression: this list was moved from an in-memory OrderBy into a SQL ORDER BY, which
        // silently swapped .NET's culture-aware comparison for SQLite's BINARY collation. That
        // orders by code point, so every capitalized name sorts before every lowercase one
        // ("Zadie" before "alice") and accented names land after "Z" - visible nonsense in the
        // autocomplete this endpoint feeds.
        await SeedBookWithAuthorAsync("Book A", "alice munro");
        await SeedBookWithAuthorAsync("Book B", "Zadie Smith");
        await SeedBookWithAuthorAsync("Book C", "Avila Author");
        await SeedBookWithAuthorAsync("Book D", "brandon Sanderson");

        var names = await _repository.GetAuthorNamesAsync();

        CollectionAssert.AreEqual(
            new List<string> { "alice munro", "Avila Author", "brandon Sanderson", "Zadie Smith" },
            names);
    }

    [TestMethod]
    public async Task GetAuthorNamesAsync_ExcludesAuthorsWithNoBooks()
    {
        // persons.name is unique, so a name can never appear on two Person rows - the Distinct()
        // in the query is belt-and-braces, and cannot be exercised from here.
        await SeedBookWithAuthorAsync("Book A", "Authoring Author");
        await _repository.GetOrCreatePerson("Orphan Author");

        var names = await _repository.GetAuthorNamesAsync();

        CollectionAssert.AreEqual(new List<string> { "Authoring Author" }, names);
    }

    // The similar-author detection shows a book count per candidate; the count query must be
    // scoped to the names it is asked about (the returned page's candidates), not the library.
    [TestMethod]
    public async Task GetAuthorBookCountsAsync_CountsOnlyTheRequestedNames()
    {
        // persons.name is unique, so the same Person instance has to back both "J.K. Rowling" books.
        var rowling = await _repository.GetOrCreatePerson("J.K. Rowling");
        var rowlingAlt = await _repository.GetOrCreatePerson("JK Rowling");

        await SeedBookWithAuthorAsync("Book One", rowling);
        await SeedBookWithAuthorAsync("Book Two", rowling);
        await SeedBookWithAuthorAsync("Book Three", rowlingAlt);
        await SeedBookWithAuthorAsync("Book Four", "Untouched Author");

        var counts = await _repository.GetAuthorBookCountsAsync(new List<string> { "J.K. Rowling", "JK Rowling" });

        Assert.AreEqual(2, counts.Count);
        Assert.AreEqual(2, counts["J.K. Rowling"]);
        Assert.AreEqual(1, counts["JK Rowling"]);
    }

    [TestMethod]
    public async Task GetAuthorBookCountsAsync_EmptyInputRequiresNoBookRows()
    {
        await SeedBookWithAuthorAsync("Book One", "Some Author");

        var counts = await _repository.GetAuthorBookCountsAsync(new List<string>());

        Assert.AreEqual(0, counts.Count);
    }
}
