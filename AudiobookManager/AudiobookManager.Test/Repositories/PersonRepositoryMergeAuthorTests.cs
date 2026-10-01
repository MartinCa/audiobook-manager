using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// <see cref="PersonRepository.MergeAuthorAsync"/> against real SQLite: it is the second half of an
/// author rename and moves rows across five tables, so the foreign-key, uniqueness and cascade
/// behavior is what the tests are really about.
/// </summary>
[TestClass]
public class PersonRepositoryMergeAuthorTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private PersonRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"personmerge-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new PersonRepository(_db);
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

    private async Task<Person> SeedPersonAsync(string name, string? matchedSourceId = null)
    {
        var person = new Person(default, name);
        if (matchedSourceId is not null)
        {
            person.MatchedSourceName = "Hardcover";
            person.MatchedSourceId = matchedSourceId;
            person.MatchedSourceUrl = $"https://hardcover.app/authors/{matchedSourceId}";
            person.LastRefreshedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        }

        _db.Persons.Add(person);
        await _db.SaveChangesAsync();
        return person;
    }

    private async Task SeedBookAsync(string title, Person? author = null, Person? narrator = null)
    {
        _db.Audiobooks.Add(new Audiobook(
            default, title, null, null, null, 2024, null, null, null, null, null, null, null, null, null,
            $"/library/{title}.m4b", $"{title}.m4b", 1000)
        {
            Authors = author is null ? new List<Person>() : new List<Person> { author },
            Narrators = narrator is null ? new List<Person>() : new List<Person> { narrator },
        });
        await _db.SaveChangesAsync();
    }

    private async Task<ExpectedBook> SeedExpectedBookAsync(string title, string sourceBookId, params (Person Person, string Name)[] authors)
    {
        var book = new ExpectedBook { SourceName = "Hardcover", SourceBookId = sourceBookId, Title = title };
        _db.ExpectedBooks.Add(book);
        await _db.SaveChangesAsync();
        foreach (var (person, name) in authors)
        {
            _db.ExpectedBookAuthors.Add(new ExpectedBookAuthor { ExpectedBookId = book.Id, PersonId = person.Id, AuthorName = name });
        }

        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<Person?> FindAsync(string name) =>
        await _db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name);

    [TestMethod]
    public async Task MergeAuthorAsync_MovesTheMatchToANewlyCreatedDestination_AndRemovesTheEmptiedSource()
    {
        var old = await SeedPersonAsync("Robert Galbraith", matchedSourceId: "204");

        var changed = await _repository.MergeAuthorAsync("Robert Galbraith", "J.K. Rowling");

        // No book references the old name, so the row is renamed in place and keeps its id.
        Assert.IsTrue(changed);
        Assert.IsNull(await FindAsync("Robert Galbraith"));
        var renamed = await FindAsync("J.K. Rowling");
        Assert.IsNotNull(renamed);
        Assert.AreEqual(old.Id, renamed.Id);
        Assert.AreEqual("204", renamed.MatchedSourceId);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_AfterTheBooksMoved_CarriesTheMatchToTheDestinationAndDeletesTheSource()
    {
        var old = await SeedPersonAsync("Robert Galbraith", matchedSourceId: "204");
        var destination = await SeedPersonAsync("J.K. Rowling");
        await SeedBookAsync("Cuckoo", author: destination);

        await _repository.MergeAuthorAsync("Robert Galbraith", "J.K. Rowling");

        Assert.IsNull(await FindAsync("Robert Galbraith"));
        var survivor = await FindAsync("J.K. Rowling");
        Assert.IsNotNull(survivor);
        Assert.AreEqual(destination.Id, survivor.Id);
        Assert.AreEqual("Hardcover", survivor.MatchedSourceName);
        Assert.AreEqual("204", survivor.MatchedSourceId);
        Assert.AreEqual("https://hardcover.app/authors/204", survivor.MatchedSourceUrl);
        Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5), survivor.LastRefreshedAt);
        Assert.AreNotEqual(old.Id, survivor.Id);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_ADestinationThatIsAlreadyMatched_KeepsItsOwnMatch()
    {
        await SeedPersonAsync("Old Name", matchedSourceId: "111");
        var destination = await SeedPersonAsync("New Name", matchedSourceId: "222");
        await SeedBookAsync("Book", author: destination);

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        Assert.AreEqual("222", (await FindAsync("New Name"))!.MatchedSourceId);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_MovesTheFollow_ButNeverCreatesASecondOne()
    {
        var old = await SeedPersonAsync("Old Name");
        var destination = await SeedPersonAsync("New Name");
        await SeedBookAsync("Book", author: destination);
        _db.AuthorFollows.Add(new AuthorFollow { PersonId = old.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        var follows = await _db.AuthorFollows.AsNoTracking().ToListAsync();
        Assert.AreEqual(1, follows.Count);
        Assert.AreEqual(destination.Id, follows.Single().PersonId);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_WhenBothAreFollowed_KeepsTheDestinationsFollowOnly()
    {
        var old = await SeedPersonAsync("Old Name");
        var destination = await SeedPersonAsync("New Name");
        await SeedBookAsync("Book", author: destination);
        _db.AuthorFollows.Add(new AuthorFollow { PersonId = old.Id, CreatedAt = DateTime.UtcNow });
        _db.AuthorFollows.Add(new AuthorFollow { PersonId = destination.Id, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        Assert.AreEqual(destination.Id, (await _db.AuthorFollows.AsNoTracking().SingleAsync()).PersonId);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_RepointsRosterLinks_RenamesThem_AndDropsOnesTheDestinationAlreadyHas()
    {
        var old = await SeedPersonAsync("Old Name");
        var destination = await SeedPersonAsync("New Name");
        await SeedBookAsync("Book", author: destination);
        var onlyOld = await SeedExpectedBookAsync("Only Old", "b1", (old, "Old Name"));
        var both = await SeedExpectedBookAsync("Both", "b2", (old, "Old Name"), (destination, "New Name"));

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        var links = await _db.ExpectedBookAuthors.AsNoTracking().ToListAsync();
        Assert.AreEqual(2, links.Count);
        Assert.IsTrue(links.All(l => l.PersonId == destination.Id && l.AuthorName == "New Name"));
        Assert.AreEqual(1, links.Count(l => l.ExpectedBookId == onlyOld.Id));
        // A book the destination already has must not end up crediting the same author twice.
        Assert.AreEqual(1, links.Count(l => l.ExpectedBookId == both.Id));
    }

    [TestMethod]
    public async Task MergeAuthorAsync_RepointsUpcomingReleases()
    {
        var old = await SeedPersonAsync("Old Name");
        var destination = await SeedPersonAsync("New Name");
        await SeedBookAsync("Book", author: destination);
        _db.UpcomingReleases.Add(new UpcomingRelease
        {
            Title = "Next", ReleaseDate = new DateOnly(2030, 1, 1), PersonId = old.Id,
            SourceName = "Hardcover", SourceBookId = "x1", DiscoveredAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        Assert.AreEqual(destination.Id, (await _db.UpcomingReleases.AsNoTracking().SingleAsync()).PersonId);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_DropsTheOldAuthorsFailedRefreshAndPendingRename()
    {
        var old = await SeedPersonAsync("Old Name");
        var destination = await SeedPersonAsync("New Name");
        await SeedBookAsync("Book", author: destination);
        _db.AuthorConsistencyIssues.Add(new AuthorConsistencyIssue { PersonId = old.Id, ErrorMessage = "boom", DetectedAt = DateTime.UtcNow });
        _db.PendingAuthorRefreshes.Add(new PendingAuthorRefresh
        {
            PersonId = old.Id, ProposedName = "New Name", SourceName = "Hardcover", FetchedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        Assert.AreEqual(0, await _db.AuthorConsistencyIssues.CountAsync());
        Assert.AreEqual(0, await _db.PendingAuthorRefreshes.CountAsync());
    }

    [TestMethod]
    public async Task MergeAuthorAsync_ASourceThatStillNarratesBooks_IsKeptButLosesItsAuthorState()
    {
        var old = await SeedPersonAsync("Old Name", matchedSourceId: "111");
        var destination = await SeedPersonAsync("New Name");
        await SeedBookAsync("Authored", author: destination);
        await SeedBookAsync("Narrated", narrator: old);

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        // Deleting it would strip the narrator off a book that was never part of the rename.
        var narrator = await FindAsync("Old Name");
        Assert.IsNotNull(narrator);
        Assert.IsNull(narrator.MatchedSourceId);
        Assert.AreEqual("111", (await FindAsync("New Name"))!.MatchedSourceId);
        Assert.AreEqual(1, await _db.Audiobooks.CountAsync(a => a.Narrators.Any(p => p.Id == old.Id)));
    }

    [TestMethod]
    public async Task MergeAuthorAsync_ANarratorOnlySourceWithNoDestinationYet_CreatesTheDestinationRatherThanRenamingTheNarrator()
    {
        var old = await SeedPersonAsync("Old Name", matchedSourceId: "111");
        await SeedBookAsync("Narrated", narrator: old);

        await _repository.MergeAuthorAsync("Old Name", "New Name");

        Assert.IsNotNull(await FindAsync("Old Name"));
        Assert.AreEqual("111", (await FindAsync("New Name"))!.MatchedSourceId);
    }

    [TestMethod]
    public async Task MergeAuthorAsync_UnknownSourceOrSameName_DoesNothing()
    {
        await SeedPersonAsync("Someone");

        Assert.IsFalse(await _repository.MergeAuthorAsync("Nobody", "Someone"));
        Assert.IsFalse(await _repository.MergeAuthorAsync("Someone", "Someone"));
        Assert.AreEqual(1, await _db.Persons.CountAsync());
    }
}
