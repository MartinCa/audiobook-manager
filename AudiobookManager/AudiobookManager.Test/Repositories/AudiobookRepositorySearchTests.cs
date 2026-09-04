using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class AudiobookRepositorySearchTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private AudiobookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"audiobookrepo-{Guid.NewGuid():N}.db");
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
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task SeedBookAsync(string bookName, string? series)
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000));
    }

    // Regression test: ranking used to happen in BrowseController, *after* the repository had
    // already truncated to `limit` in plain alphabetical order - so the one series that actually
    // starts with the query was discarded before anything could rank it. Fails against the
    // pre-fix repository, which returns the five alphabetically-first matches and never
    // includes "Harry Potter".
    [TestMethod]
    public async Task SearchSeriesAsync_RanksPrefixMatchesBeforeApplyingTheLimit()
    {
        // All six contain "charm", so the LIKE matches every one of them and the limit of 5 has
        // to drop one. Only "charm school" *starts* with it - and its lowercase initial sorts
        // after every uppercase one under SQLite's BINARY collation, so plain alphabetical
        // ordering is exactly what drops it.
        foreach (var series in new[] { "Alex Rider charm", "Bosch charm", "Cormoran charm", "Dresden charm", "Expanse charm" })
        {
            await SeedBookAsync($"{series} book", series);
        }
        await SeedBookAsync("charm school book", "charm school");

        var results = await _repository.SearchSeriesAsync("charm", 5);

        Assert.AreEqual(5, results.Count);
        Assert.AreEqual("charm school", results[0].Series);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_PrefixMatchIsRankedFirstAmongSeveralMatches()
    {
        await SeedBookAsync("b", "The Sanderson Files");
        await SeedBookAsync("c", "Sanditon");

        var results = await _repository.SearchSeriesAsync("sand", 5);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("Sanditon", results[0].Series);
        Assert.AreEqual("The Sanderson Files", results[1].Series);
    }

    // Same truncation bug on the book search: the type-ahead asks for 5 rows, and ordering by
    // title alone threw away the prefix match before the controller could rank it.
    [TestMethod]
    public async Task SearchAsync_RanksPrefixMatchesBeforeApplyingTheLimit()
    {
        foreach (var title in new[] { "Alex charm", "Bosch charm", "Cormoran charm", "Dresden charm", "Expanse charm" })
        {
            await SeedBookAsync(title, null);
        }
        await SeedBookAsync("Charm School", null);

        var (items, total) = await _repository.SearchAsync("charm", 5, 0);

        Assert.AreEqual(6, total);
        Assert.AreEqual(5, items.Count);
        Assert.AreEqual("Charm School", items[0].BookName);
    }

    // The ranking ORDER BY includes a correlated subquery over the authors collection, and this
    // query uses AsSplitQuery with Skip/Take - so the author branch needs exercising too, not
    // just the BookName one.
    [TestMethod]
    public async Task SearchAsync_RanksAMatchOnAnAuthorNamePrefixAheadOfASubstringMatch()
    {
        var prefixAuthorBook = new Audiobook(
            default, "zzz last by title", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/zzz.m4b", "zzz.m4b", 1000);
        prefixAuthorBook.Authors.Add(new Person(default, "Sanderson Brandon"));
        await _repository.InsertAudiobook(prefixAuthorBook);

        var substringBook = new Audiobook(
            default, "aaa first by title", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/aaa.m4b", "aaa.m4b", 1000);
        substringBook.Authors.Add(new Person(default, "Brandon Sanderson"));
        await _repository.InsertAudiobook(substringBook);

        var (items, total) = await _repository.SearchAsync("sanderson", 10, 0);

        Assert.AreEqual(2, total);
        Assert.AreEqual("zzz last by title", items[0].BookName);
        // The include graph still comes back intact alongside the ranked ordering.
        Assert.AreEqual("Sanderson Brandon", items[0].Authors.Single().Name);
    }

    // Regression for review feedback on #1303: includeNarratorsAndGenres must actually gate those
    // two Includes, not just exist as an unused parameter - the default (true) is what the paged
    // /audiobooks/search endpoint relies on to populate MapToSummaryDto's Narrators/Genres fields.
    [TestMethod]
    public async Task SearchAsync_DefaultIncludesNarratorsAndGenres()
    {
        var book = new Audiobook(
            default, "Wind and Truth", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/wat.m4b", "wat.m4b", 1000);
        book.Authors.Add(new Person(default, "Brandon Sanderson"));
        book.Narrators.Add(new Person(default, "Michael Kramer"));
        book.Genres.Add(new Genre(default, "Fantasy"));
        await _repository.InsertAudiobook(book);

        var (items, _) = await _repository.SearchAsync("wind", 10, 0);

        Assert.AreEqual("Michael Kramer", items[0].Narrators.Single().Name);
        Assert.AreEqual("Fantasy", items[0].Genres.Single().Name);
    }

    // The type-ahead path (BrowseController.SearchLibrary) only ever reads Item.Authors from its
    // hits, so it asks for includeNarratorsAndGenres: false. Authors must still come back - it's
    // unconditionally included and is what the ranking ORDER BY matches on.
    [TestMethod]
    public async Task SearchAsync_NarratorsAndGenresExcluded_StillReturnsAuthors()
    {
        var book = new Audiobook(
            default, "Wind and Truth", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/wat.m4b", "wat.m4b", 1000);
        book.Authors.Add(new Person(default, "Brandon Sanderson"));
        book.Narrators.Add(new Person(default, "Michael Kramer"));
        book.Genres.Add(new Genre(default, "Fantasy"));
        await _repository.InsertAudiobook(book);

        var (items, total) = await _repository.SearchAsync(
            "wind", 10, 0, includeTotal: false, includeNarratorsAndGenres: false);

        Assert.AreEqual(0, total, "includeTotal: false must come back as the documented sentinel");
        Assert.AreEqual("Brandon Sanderson", items[0].Authors.Single().Name);
        Assert.AreEqual(0, items[0].Narrators.Count);
        Assert.AreEqual(0, items[0].Genres.Count);
    }

    // Regression test: this compared paths with a raw SQL `==`, which SQLite evaluates under its
    // BINARY collation - so on a case-insensitive volume a stored path differing only in case
    // from the generated target (an author's casing edited in the tags since import) was not
    // found, and the duplicate check reported an untracked file instead of the tracked book.
    // Fails against the pre-fix repository, which returns null.
    [TestMethod]
    public async Task GetByFullPathAsync_CaseDifferingStoredPath_IsFoundWithACaseInsensitiveComparison()
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, "Children of Time", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/Adrian Tchaikovsky/Children of Time.m4b", "Children of Time.m4b", 1000));

        var result = await _repository.GetByFullPathAsync(
            "/library/adrian tchaikovsky/children of time.m4b",
            (a, b) => string.Equals(
                Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase));

        Assert.IsNotNull(result);
        Assert.AreEqual("Children of Time", result.BookName);
    }

    [TestMethod]
    public async Task GetByFullPathAsync_CaseDifferingStoredPath_IsNotFoundWithACaseSensitiveComparison()
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, "Children of Time", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/Adrian Tchaikovsky/Children of Time.m4b", "Children of Time.m4b", 1000));

        var result = await _repository.GetByFullPathAsync(
            "/library/adrian tchaikovsky/children of time.m4b",
            (a, b) => string.Equals(a, b, StringComparison.Ordinal));

        Assert.IsNull(result);
    }

    // A literal '_' in a file name (extremely common) must not act as a LIKE wildcard and pull
    // in a different book that happens to differ in that one position.
    [TestMethod]
    public async Task GetByFullPathAsync_UnderscoreInFileName_IsNotTreatedAsAWildcard()
    {
        await _repository.InsertAudiobook(new Audiobook(
            default, "Wrong Book", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/aXb.m4b", "aXb.m4b", 1000));

        var result = await _repository.GetByFullPathAsync(
            "/library/a_b.m4b",
            (a, b) => string.Equals(a, b, StringComparison.Ordinal));

        Assert.IsNull(result);
    }

    // Regression test: UpdateAudiobookAsync called DbSet.Update() on an entity the caller had
    // just loaded from this same context. On the *root* that marks every property modified
    // regardless of what changed, so saving one book rewrote all ~18 columns - the whole
    // description blob included - instead of the one field the user edited.
    //
    // (Related entities already tracked are not affected: EF's graph traversal only paints
    // nodes whose state is Detached. An earlier version of this test asserted otherwise and
    // passed against the unfixed code, which is why it now asserts on the root's property
    // states - and those are sampled during SaveChanges, since it resets them afterwards.)
    [TestMethod]
    public async Task UpdateAudiobookAsync_TrackedEntity_OnlyWritesTheColumnsThatChanged()
    {
        var person = new Person(default, "Adrian Tchaikovsky");
        var book = new Audiobook(
            default, "Children of Time", null, null, null, 2016,
            null, null, null, null, null, null, null, null, null,
            "/library/Children of Time.m4b", "Children of Time.m4b", 1000);
        book.Authors.Add(person);
        await _repository.InsertAudiobook(book);

        var loaded = await _repository.GetByIdWithIncludesAsync(book.Id);
        Assert.IsNotNull(loaded);
        loaded.BookName = "Children of Time (Revised)";

        List<string> modifiedProperties = null!;
        void CaptureModifiedProperties(object? sender, SavingChangesEventArgs args) =>
            modifiedProperties = _db.Entry(loaded).Properties
                .Where(prop => prop.IsModified)
                .Select(prop => prop.Metadata.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        _db.SavingChanges += CaptureModifiedProperties;
        try
        {
            await _repository.UpdateAudiobookAsync(loaded);
        }
        finally
        {
            _db.SavingChanges -= CaptureModifiedProperties;
        }

        Assert.IsNotNull(modifiedProperties, "SaveChanges should have run");
        CollectionAssert.AreEqual(new[] { nameof(Audiobook.BookName) }, modifiedProperties);

        // ...and the edit itself still persisted.
        var reread = await _repository.GetByIdWithIncludesAsync(book.Id);
        Assert.AreEqual("Children of Time (Revised)", reread!.BookName);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_GroupsMatchingSeriesWithBookCounts()
    {
        await SeedBookAsync("Mistborn 1", "Mistborn");
        await SeedBookAsync("Mistborn 2", "Mistborn");
        await SeedBookAsync("Elantris", null);
        await SeedBookAsync("Something Else", "Wheel of Time");

        var results = await _repository.SearchSeriesAsync("mist", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Mistborn", results[0].Series);
        Assert.AreEqual(2, results[0].BookCount);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_ReturnsEmptyWhenNoSeriesMatch()
    {
        await SeedBookAsync("Elantris", null);
        await SeedBookAsync("Something Else", "Wheel of Time");

        var results = await _repository.SearchSeriesAsync("nonexistent", 10);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_UnaccentedQueryMatchesAccentedSeriesName()
    {
        // SQLite's default BINARY collation (which LIKE uses) never folds diacritics, so typing
        // "cafe" for "Café" would otherwise return nothing.
        await SeedBookAsync("Café Noir 1", "Café Noir");

        var results = await _repository.SearchSeriesAsync("cafe", 10);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Café Noir", results[0].Series);
    }

    [TestMethod]
    public async Task SearchAsync_UnaccentedQueryMatchesAccentedBookName()
    {
        await SeedBookAsync("Émigré", null);

        var (items, total) = await _repository.SearchAsync("emigre", 10, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Émigré", items[0].BookName);
    }

    [TestMethod]
    public async Task SearchAsync_AccentedQueryMatchesUnaccentedBookName()
    {
        await SeedBookAsync("Emigre", null);

        var (items, total) = await _repository.SearchAsync("émigré", 10, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual("Emigre", items[0].BookName);
    }

    [TestMethod]
    public async Task SearchSeriesAsync_RespectsLimit()
    {
        await SeedBookAsync("Book A", "Series Alpha");
        await SeedBookAsync("Book B", "Series Beta");
        await SeedBookAsync("Book C", "Series Gamma");

        var results = await _repository.SearchSeriesAsync("series", 2);

        Assert.AreEqual(2, results.Count);
    }

    [TestMethod]
    public async Task GetByFullPathAsync_MatchingBookExists_ReturnsIt()
    {
        await SeedBookAsync("Children of Time", null);

        var result = await _repository.GetByFullPathAsync("/library/Children of Time.m4b");

        Assert.IsNotNull(result);
        Assert.AreEqual("Children of Time", result.BookName);
    }

    [TestMethod]
    public async Task GetByFullPathAsync_NoBookAtThatPath_ReturnsNull()
    {
        await SeedBookAsync("Children of Time", null);

        var result = await _repository.GetByFullPathAsync("/library/Someone Else.m4b");

        Assert.IsNull(result);
    }

    // Regression for #1303: the accent-folded shadow column search now reads from (BookNameFolded
    // etc.) has to be kept in sync by AccentFoldedColumnsInterceptor on every save, not just at
    // insert time - a rename that never updates the folded column would make the old name
    // searchable forever and the new one never findable.
    [TestMethod]
    public async Task SearchAsync_AfterRenamingABook_FindsItByTheNewNameAndNotTheOld()
    {
        await SeedBookAsync("Original Título", null);

        var (found, _) = await _repository.SearchAsync("titulo", 10, 0);
        var loaded = await _repository.GetByIdWithIncludesAsync(found.Single().Id);
        loaded!.BookName = "Renamed Título";
        await _repository.UpdateAudiobookAsync(loaded);

        var (byOldName, _) = await _repository.SearchAsync("original", 10, 0);
        Assert.AreEqual(0, byOldName.Count, "the old name must no longer match after the rename");

        var (byNewName, _) = await _repository.SearchAsync("renamed", 10, 0);
        Assert.AreEqual(1, byNewName.Count);
        Assert.AreEqual("Renamed Título", byNewName[0].BookName);

        // Unaccented query still matches the accented new title, proving the folded column (not
        // just the raw one) was recomputed.
        var (byFoldedNewName, _) = await _repository.SearchAsync("titulo", 10, 0);
        Assert.IsTrue(byFoldedNewName.Any(a => a.BookName == "Renamed Título"));
    }

    // Regression for #1303: a new Person can enter the change tracker without ever going through
    // PersonRepository, by being attached to an Audiobook's Authors collection and cascade-inserted
    // alongside it. AccentFoldedColumnsInterceptor has to catch that path too, not just
    // PersonRepository.GetOrCreatePerson(s).
    [TestMethod]
    public async Task SearchAsync_AuthorAddedByCascadeInsertThroughAudiobook_IsFoldedAndSearchable()
    {
        var book = new Audiobook(
            default, "Le Petit Prince", null, null, null, 1943,
            null, null, null, null, null, null, null, null, null,
            "/library/Le Petit Prince.m4b", "Le Petit Prince.m4b", 1000)
        {
            Authors = new List<Person> { new Person(default, "Antoine de Saint-Exupéry") }
        };

        await _repository.InsertAudiobook(book);

        var (items, _) = await _repository.SearchAsync("exupery", 10, 0);

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("Le Petit Prince", items[0].BookName);
    }

    // Regression for review feedback on #1303: the AddAccentFoldedSearchColumns migration's
    // backfill (UPDATE ... SET book_name_folded = fold_accents(book_name), ...) only ever ran
    // through a manual smoke test, not the committed suite - and it depends on fold_accents being
    // registered on the connection that runs the migration, via AccentFoldingConnectionInterceptor
    // (DatabaseContext.OnConfiguring). Rebuilt here through the real migrator (mirroring
    // GenreRepositoryTests.AddUniqueGenreNameIndexMigration_*), seeding rows *before* this
    // migration runs, to prove that dependency actually holds for this codebase's DatabaseContext
    // construction pattern rather than trusting the comment that asserts it.
    [TestMethod]
    public async Task AddAccentFoldedSearchColumnsMigration_BackfillsExistingRows()
    {
        var migratedPath = Path.Combine(Path.GetTempPath(), $"foldbackfill-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = migratedPath });

        try
        {
            using (var seed = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings))
            {
                // Migrate up to (but not including) the folded columns, then plant rows with
                // diacritics for the backfill to fold.
                await seed.Database.MigrateAsync("AddUniqueGenreNameIndex");

                await seed.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO audiobooks (id, book_name, subtitle, series, description, year, file_info_full_path, file_info_file_name, file_info_size_in_bytes)
                    VALUES (1, 'René''s Adventure', 'Ólafur''s Tale', 'Été', 'A story about René', 2020, '/library/rene.m4b', 'rene.m4b', 1);
                    """);
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO persons (id, name) VALUES (1, 'Antoine de Saint-Exupéry');");
            }

            using (var migrated = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings))
            {
                await migrated.Database.MigrateAsync();

                var book = await migrated.Audiobooks.AsNoTracking().SingleAsync(a => a.Id == 1);
                Assert.AreEqual("Rene's Adventure", book.BookNameFolded);
                Assert.AreEqual("Olafur's Tale", book.SubtitleFolded);
                Assert.AreEqual("Ete", book.SeriesFolded);
                Assert.AreEqual("A story about Rene", book.DescriptionFolded);

                var person = await migrated.Persons.AsNoTracking().SingleAsync(p => p.Id == 1);
                Assert.AreEqual("Antoine de Saint-Exupery", person.NameFolded);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(migratedPath))
            {
                File.Delete(migratedPath);
            }
        }
    }
}
