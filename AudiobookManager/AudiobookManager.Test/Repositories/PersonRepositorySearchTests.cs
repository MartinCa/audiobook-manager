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
        await SeedBookWithNarratorAsync(bookName, new Person(default, narratorName));
    }

    private async Task SeedBookWithNarratorAsync(string bookName, Person narrator)
    {
        var audiobook = new Audiobook(
            default, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, $"Placeholder Author for {bookName}") },
            Narrators = new List<Person> { narrator }
        };

        await _audiobookRepository.InsertAudiobook(audiobook);
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

    // ---- Entry-status backing queries ----

    [TestMethod]
    public async Task FindAuthorByFoldedNameAsync_AccentAndCaseInsensitive()
    {
        await SeedBookWithAuthorAsync("Book One", "René Descartes");

        var found = await _repository.FindAuthorByFoldedNameAsync("rene descartes");

        Assert.IsNotNull(found);
        Assert.AreEqual("René Descartes", found.Name);
    }

    [TestMethod]
    public async Task FindAuthorByFoldedNameAsync_NoSuchAuthor_ReturnsNull()
    {
        await SeedBookWithAuthorAsync("Book One", "Someone Else");

        var found = await _repository.FindAuthorByFoldedNameAsync("Nobody Here");

        Assert.IsNull(found);
    }

    // Regression guard for the bounded prefilter: the entry-status "similar" classification must
    // surface a candidate that shares only the first token (the common typo shape) without
    // loading the whole name list - a plain containment prefilter on the query would never
    // return "Brandon Sanderson" for a misspelled surname.
    [TestMethod]
    public async Task SearchAuthorNamesAsync_FirstTokenVariant_SurfacesAfterFullContainment()
    {
        await SeedBookWithAuthorAsync("A", "Brandon Sanderson");
        await SeedBookWithAuthorAsync("B", "Brandon The Retriever");

        var results = await _repository.SearchAuthorNamesAsync("Brandon Sandersson", 10);

        CollectionAssert.Contains(results.Select(r => r.Name).ToList(), "Brandon Sanderson");
    }

    [TestMethod]
    public async Task FindAuthorByFoldedNameAsync_BlankInput_ReturnsNull()
    {
        var found = await _repository.FindAuthorByFoldedNameAsync("   ");

        Assert.IsNull(found);
    }

    // Regression guard: an exact "existing author" answer must not be given for a person that does
    // not author books. Narrator-only persons and orphan (bookless) person rows exist alongside
    // authors in the shared persons table; classifying one of them as an existing author would
    // make the entry-status indicator lie ("Brandon Sanderson" applies, "read by X" does not).
    [TestMethod]
    public async Task FindAuthorByFoldedNameAsync_NarratorOnlyOrOrphanPerson_IsNotAnExistingAuthor()
    {
        await SeedBookWithNarratorAsync("Book With Narrator", "Narrator Only");
        // An orphan person row: persisted by a race / a cleared book, but linked to no book.
        await _repository.GetOrCreatePerson("Orphan Person");

        Assert.IsNull(await _repository.FindAuthorByFoldedNameAsync("Narrator Only"),
            "a narrator-only person is not an author");
        Assert.IsNull(await _repository.FindAuthorByFoldedNameAsync("Orphan Person"),
            "a person with no books at all is not an author");
    }

    // Regression guard for the bounded prefilter: LIKE wildcards typed by the user ('%' and '_')
    // must not act as wildcards - "B_eta" must match a name containing a literal underscore, and
    // "100%" must not match every name merely containing "100".
    [TestMethod]
    public async Task SearchAuthorNamesAsync_LikeWildcardsInTheQuery_AreTreatedLiterally()
    {
        await SeedBookWithAuthorAsync("Book A", "B_eta Helper");
        await SeedBookWithAuthorAsync("Book B", "Breta Helper");   // matches unescaped B_eta, not the literal
        await SeedBookWithAuthorAsync("Book C", "100% Author");
        await SeedBookWithAuthorAsync("Book D", "100 Friends");    // matches unescaped 100%, not the literal

        var underscore = await _repository.SearchAuthorNamesAsync("B_eta", 10);
        CollectionAssert.AreEqual(
            new List<string> { "B_eta Helper" },
            underscore.Select(r => r.Name).ToList(),
            "an underscore in the query matches a literal underscore, not 'any character'");

        var percent = await _repository.SearchAuthorNamesAsync("100%", 10);
        CollectionAssert.AreEqual(
            new List<string> { "100% Author" },
            percent.Select(r => r.Name).ToList(),
            "'%' in the query matches a literal '%', not a wildcard");
    }

    // Regression test for the narrator book-count projection: FindNarratorByFoldedNameAsync used
    // to project BooksAuthored.Count regardless of role, so a narrator-only person's row reported
    // 0 while a person who also authored came back with the wrong number. The count must follow
    // the role, and the author path must keep counting authored books even for the same person.
    [TestMethod]
    public async Task FindNarratorByFoldedNameAsync_CountsNarratedBooks_NotAuthoredBooks()
    {
        var person = await _repository.GetOrCreatePerson("Jane Narrator");
        await SeedBookWithNarratorAsync("Narrated One", person);
        await SeedBookWithNarratorAsync("Narrated Two", person);
        await SeedBookWithAuthorAsync("Authored One", person);

        var narrator = await _repository.FindNarratorByFoldedNameAsync("jane narrator");

        Assert.IsNotNull(narrator);
        Assert.AreEqual(2, narrator.BookCount,
            "a narrator lookup must count narrated books, not authored ones");

        var author = await _repository.FindAuthorByFoldedNameAsync("jane narrator");

        Assert.IsNotNull(author);
        Assert.AreEqual(1, author.BookCount,
            "the author path must keep counting authored books");
    }

    // Same role-scoped count on the bounded candidate search: SearchNarratorNamesAsync used to
    // project BooksAuthored.Count, so the narrator candidate list reported 0 for every narrator.
    [TestMethod]
    public async Task SearchNarratorNamesAsync_CountsNarratedBooks_NotAuthoredBooks()
    {
        var person = await _repository.GetOrCreatePerson("Brandon Narrator");
        await SeedBookWithNarratorAsync("Narrated One", person);
        await SeedBookWithNarratorAsync("Narrated Two", person);
        await SeedBookWithAuthorAsync("Authored One", person);

        var narrators = await _repository.SearchNarratorNamesAsync("Brandon", 10);

        Assert.AreEqual(1, narrators.Count);
        Assert.AreEqual(2, narrators[0].BookCount,
            "a narrator candidate must report narrated books, not authored ones");

        var authors = await _repository.SearchAuthorNamesAsync("Brandon", 10);

        Assert.AreEqual(1, authors.Count);
        Assert.AreEqual(1, authors[0].BookCount,
            "the author path must keep counting authored books");
    }

    [TestMethod]
    public async Task GetAuthorSummariesPagedAsync_LikeWildcardsInTheSearch_AreTreatedLiterally()
    {
        await SeedBookWithAuthorAsync("Book A", "B_eta Helper");
        await SeedBookWithAuthorAsync("Book B", "Breta Helper");
        await SeedBookWithAuthorAsync("Book C", "100% Author");
        await SeedBookWithAuthorAsync("Book D", "100 Friends");

        var (underscore, totalUnderscore) = await _repository.GetAuthorSummariesPagedAsync("B_eta", 10, 0);
        CollectionAssert.AreEqual(
            new List<string> { "B_eta Helper" },
            underscore.Select(r => r.Name).ToList(),
            "an underscore in the paged search matches a literal underscore, not 'any character'");
        Assert.AreEqual(1, totalUnderscore);

        var (percent, totalPercent) = await _repository.GetAuthorSummariesPagedAsync("100%", 10, 0);
        CollectionAssert.AreEqual(
            new List<string> { "100% Author" },
            percent.Select(r => r.Name).ToList(),
            "'%' in the paged search matches a literal '%', not a wildcard");
        Assert.AreEqual(1, totalPercent);
    }

    [TestMethod]
    public async Task SearchAuthorSummariesAsync_LikeWildcardsInTheQuery_AreTreatedLiterally()
    {
        await SeedBookWithAuthorAsync("Book A", "B_eta Helper");
        await SeedBookWithAuthorAsync("Book B", "Breta Helper");
        await SeedBookWithAuthorAsync("Book C", "100% Author");
        await SeedBookWithAuthorAsync("Book D", "100 Friends");

        var (underscore, _) = await _repository.SearchAuthorSummariesAsync("B_eta", 10, 0);
        CollectionAssert.AreEqual(
            new List<string> { "B_eta Helper" },
            underscore.Select(r => r.Name).ToList(),
            "an underscore in the search matches a literal underscore, not 'any character'");

        var (percent, _) = await _repository.SearchAuthorSummariesAsync("100%", 10, 0);
        CollectionAssert.AreEqual(
            new List<string> { "100% Author" },
            percent.Select(r => r.Name).ToList(),
            "'%' in the search matches a literal '%', not a wildcard");
    }

    // ---- Narrator entry-status backing queries ----

    [TestMethod]
    public async Task FindNarratorByFoldedNameAsync_AccentAndCaseInsensitive()
    {
        await SeedBookWithNarratorAsync("Book One", "René Descartes");
        await SeedBookWithNarratorAsync("Book Two", "Someone Else");

        var found = await _repository.FindNarratorByFoldedNameAsync("rene descartes");

        Assert.IsNotNull(found);
        Assert.AreEqual("René Descartes", found.Name);
    }

    [TestMethod]
    public async Task FindNarratorByFoldedNameAsync_AuthorOnlyOrOrphanPerson_IsNotAnExistingNarrator()
    {
        await SeedBookWithAuthorAsync("Book With Author", "Author Only");
        await _repository.GetOrCreatePerson("Orphan Person");

        Assert.IsNull(await _repository.FindNarratorByFoldedNameAsync("Author Only"),
            "an author-only person is not a narrator");
        Assert.IsNull(await _repository.FindNarratorByFoldedNameAsync("Orphan Person"),
            "a person with no books at all is not a narrator");
    }

    [TestMethod]
    public async Task SearchNarratorNamesAsync_FirstTokenVariant_SurfacesAfterFullContainment()
    {
        await SeedBookWithNarratorAsync("A", "Brandon Sanderson");
        await SeedBookWithNarratorAsync("B", "Brandon The Retriever");

        var results = await _repository.SearchNarratorNamesAsync("Brandon Sandersson", 10);

        CollectionAssert.Contains(results.Select(r => r.Name).ToList(), "Brandon Sanderson");
    }

    [TestMethod]
    public async Task SearchNarratorNamesAsync_LikeWildcardsInTheQuery_AreTreatedLiterally()
    {
        await SeedBookWithNarratorAsync("Book A", "B_eta Helper");
        await SeedBookWithNarratorAsync("Book B", "Breta Helper");

        var underscore = await _repository.SearchNarratorNamesAsync("B_eta", 10);

        CollectionAssert.AreEqual(
            new List<string> { "B_eta Helper" },
            underscore.Select(r => r.Name).ToList(),
            "an underscore in a narrator query matches a literal underscore, not 'any character'");
    }
}
