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
    private ExpectedBookRepository _expectedBookRepository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _expectedBookRepository = new ExpectedBookRepository(_db);
        _repository = new SeriesRepository(_db, _expectedBookRepository);
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

        await SeedBooksAsync(series.Id, new List<ExpectedBook>
        {
            new() { Title = "The Final Empire", SeriesPosition = "1" },
            new() { Title = "The Well of Ascension", SeriesPosition = "2" },
            new() { Title = "Secret History", SeriesPosition = "3.5" },
        });

        return series;
    }

    private async Task SeedBooksAsync(long seriesId, List<ExpectedBook> books)
    {
        // The roster lives on the unified expected_books table; the legacy per-series
        // series_expected_books table was dropped once the migration copy landed.
        var now = DateTime.UtcNow;
        foreach (var book in books)
        {
            _db.ExpectedBooks.Add(new ExpectedBook
            {
                SourceName = "Hardcover",
                Title = book.Title,
                SeriesPosition = book.SeriesPosition,
                IsIgnored = book.IsIgnored,
                IsCompilation = book.IsCompilation,
                SeriesId = seriesId,
                FirstSeenAt = now,
                LastRefreshedAt = now,
            });
        }

        await _db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_FlagsTheEntryMatchingTheNaturalKey()
    {
        var series = await SeedSeriesAsync();

        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true, 10);

        var stored = await GetRosterViewAsync("Mistborn");
        Assert.IsTrue(stored.ExpectedBooks.Single(b => b.Title == "Secret History").IsIgnored);
        Assert.IsFalse(stored.ExpectedBooks.Where(b => b.Title != "Secret History").Any(b => b.IsIgnored));
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_StillHitsTheSameLogicalBookAfterAnInPlaceReMatch()
    {
        var series = await SeedSeriesAsync();
        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true, 10);

        var idBefore = (await GetRosterViewAsync("Mistborn"))
            .ExpectedBooks.Single(b => b.Title == "Secret History").Id;

        // Re-match the same three-book roster through the unified upsert - the same path
        // MatchSeriesCoreAsync uses. The rows are refreshed IN PLACE (UpsertAsync never deletes
        // a row or resets IsIgnored), so the ignore decision survives and the row id is stable
        // rather than a cached id being unsafe.
        var upserts = new List<ExpectedBookUpsert>
        {
            MakeSeriesUpsert(series, "The Final Empire", "1"),
            MakeSeriesUpsert(series, "The Well of Ascension", "2"),
            MakeSeriesUpsert(series, "Secret History", "3.5"),
        };
        await _expectedBookRepository.UpsertManyAsync(upserts);

        var refreshed = await GetRosterViewAsync("Mistborn");
        var secretHistoryAfter = refreshed.ExpectedBooks.Single(b => b.Title == "Secret History");
        Assert.AreEqual(idBefore, secretHistoryAfter.Id,
            "an in-place re-match keeps the row, so its id is stable");
        Assert.IsTrue(secretHistoryAfter.IsIgnored,
            "the ignore decision the user made on the same logical book survives an in-place re-match");

        // Unignoring by the natural key finds the current row, whatever its id is now.
        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", false, 10);

        var afterUnignore = await GetRosterViewAsync("Mistborn");
        Assert.IsFalse(afterUnignore.ExpectedBooks.Single(b => b.Title == "Secret History").IsIgnored);
        Assert.IsFalse(afterUnignore.ExpectedBooks.Any(b => b.IsIgnored));
    }

    private static ExpectedBookUpsert MakeSeriesUpsert(Series series, string title, string position) =>
        new(
            SourceName: "Hardcover",
            SourceBookId: "hc-" + title,
            Title: title,
            Year: null,
            ReleaseDate: null,
            SourceUrl: null,
            ImageUrl: null,
            SeriesId: series.Id,
            SourceSeriesId: series.MatchedSourceId,
            SourceSeriesName: series.MatchedSeriesName,
            SeriesPosition: position,
            IsCompilation: false,
            Authors: new List<ExpectedBookAuthorLink>());

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_FallsBackToTheTitleWhenTheEntryHasNoPosition()
    {
        var series = await _repository.UpsertSeriesAsync(new Series { Name = "Standalones" });
        await SeedBooksAsync(series.Id, new List<ExpectedBook>
        {
            new() { Title = "A Book Without A Position" },
        });

        await _repository.SetExpectedBookIgnoredAsync("Standalones", null, "A Book Without A Position", true, 10);

        var stored = await GetRosterViewAsync("Standalones");
        Assert.IsTrue(stored.ExpectedBooks.Single().IsIgnored);
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
                var repository = new SeriesRepository(context, new ExpectedBookRepository(context));
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
            () => _repository.SetExpectedBookIgnoredAsync("Mistborn", "99", "Nonexistent", true, 10));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredAsync("Unknown Series", "1", "Whatever", true, 10));
    }

    // The ignore-path roster read is bounded to maxBooks + 1 rows like the sibling reads, and
    // the flag write is set-based (safe against a concurrent refresh's unlink/orphan-delete). A
    // roster past the cap degrades safely: an entry the natural key resolves WITHIN the readable
    // prefix is still updated, and one beyond the prefix is reported not-found - never a guess.
    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_RosterPastTheCap_StillTouchesTheVisibleMatch()
    {
        await SeedSeriesAsync();

        await _repository.SetExpectedBookIgnoredAsync("Mistborn", "2", "The Well of Ascension", true, 2);

        var stored = await GetRosterViewAsync("Mistborn");
        Assert.IsTrue(stored.ExpectedBooks.Single(b => b.Title == "The Well of Ascension").IsIgnored);
        Assert.AreEqual(1, stored.ExpectedBooks.Count(b => b.IsIgnored));
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredAsync_RosterPastTheCap_NotFoundWhenTheEntryLiesBeyondThePrefix()
    {
        await SeedSeriesAsync();

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true, 2));
    }

    [TestMethod]
    public async Task FindExpectedBookAsync_ResolvesByPositionAndTitle()
    {
        var series = await SeedSeriesAsync();

        var book = await _repository.FindExpectedBookAsync("Mistborn", "1", "The Final Empire");

        Assert.IsNotNull(book);
        Assert.AreEqual("The Final Empire", book.Title);
        Assert.AreEqual("1", book.SeriesPosition);
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
        Assert.AreEqual("3.5", book.SeriesPosition);
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

    [TestMethod]
    public async Task GetNameByIdAsync_ReturnsTheNameOrNull()
    {
        var series = await SeedSeriesAsync();

        Assert.AreEqual("Mistborn", await _repository.GetNameByIdAsync(series.Id));
        Assert.IsNull(await _repository.GetNameByIdAsync(999_999));
    }

    private async Task<(Series Series, List<ExpectedBook> Books)> SeedRosterAsync(string name, int bookCount)
    {
        var series = await _repository.UpsertSeriesAsync(new Series { Name = name });
        var books = Enumerable.Range(1, bookCount)
            .Select(i => new ExpectedBook { Title = $"Book {i:00}", SeriesPosition = (i % 7).ToString() })
            .ToList();
        await SeedBooksAsync(series.Id, books);
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

    // Regression for the adoption review finding: a fully successful source-name adoption must
    // migrate the matched catalog row to the new name - roster (ignore flags included), matched
    // metadata, omnibus setting and last-refreshed all follow - and leave nothing addressable
    // under the old name. The old name otherwise stays a matched zombie row in the overview with
    // the whole roster reported missing while the adopted name owns no roster at all.
    [TestMethod]
    public async Task RenameAsync_MigratesCatalogRowRosterAndMetadataToTheNewName()
    {
        var series = await _repository.UpsertSeriesAsync(new Series
        {
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            MatchedSourceUrl = "https://hardcover.app/series/42",
            MatchedSeriesName = "Mistborn Saga",
            MatchConfidence = 0.9,
            LastRefreshedAt = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            IncludeOmnibusEditions = true,
        });
        await SeedBooksAsync(series.Id, new List<ExpectedBook>
        {
            new() { Title = "The Final Empire", SeriesPosition = "1" },
            new() { Title = "Secret History", SeriesPosition = "3.5", IsIgnored = true },
        });

        var renamed = await _repository.RenameAsync("Mistborn", "Mistborn Saga");

        // The old name no longer resolves through either read shape.
        Assert.IsNull(await _repository.GetByNameAsync("Mistborn"));
        Assert.IsNull(await _repository.GetByNameWithExpectedBooksAsync("Mistborn"));

        var underNewName = await _repository.GetByNameWithExpectedBooksAsync("Mistborn Saga");
        Assert.IsNotNull(underNewName);
        Assert.AreEqual(renamed.Id, underNewName!.Id, "the row keeps its id - only the name changes");
        Assert.AreEqual("Hardcover", underNewName.MatchedSourceName);
        Assert.AreEqual("42", underNewName.MatchedSourceId);
        Assert.AreEqual("https://hardcover.app/series/42", underNewName.MatchedSourceUrl);
        Assert.AreEqual("Mistborn Saga", underNewName.MatchedSeriesName);
        Assert.AreEqual(0.9, underNewName.MatchConfidence);
        Assert.AreEqual(new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc), underNewName.LastRefreshedAt,
            "last-refreshed must survive the rename");
        Assert.IsTrue(underNewName.IncludeOmnibusEditions);
        Assert.AreEqual(2, underNewName.ExpectedBooks.Count);
        Assert.IsTrue(underNewName.ExpectedBooks.Single(b => b.Title == "Secret History").IsIgnored,
            "the ignore flag must survive the rename");

        // The migrated row is fully re-addressable: natural-key lookups and ignore flips now
        // resolve under the new name, which is what keeps detail/refresh/candidates working.
        Assert.IsNotNull(await _repository.FindExpectedBookAsync("Mistborn Saga", "1", "The Final Empire"));
        Assert.IsNotNull(await _repository.FindExpectedBookStrictAsync("Mistborn Saga", "3.5", "Secret History"));
        await _repository.SetExpectedBookIgnoredAsync("Mistborn Saga", "3.5", "Secret History", false, 10);
        Assert.IsFalse((await GetRosterViewAsync("Mistborn Saga"))
            .ExpectedBooks.Single(b => b.Title == "Secret History").IsIgnored);
    }

    [TestMethod]
    public async Task RenameAsync_RefusesWhenTheDestinationNameIsAlreadyACatalogRow()
    {
        await _repository.UpsertSeriesAsync(new Series { Name = "Mistborn" });
        await _repository.UpsertSeriesAsync(new Series { Name = "Mistborn Saga" });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _repository.RenameAsync("Mistborn", "Mistborn Saga"));

        // Neither row was clobbered: both names still resolve on their own rows.
        Assert.IsNotNull(await _repository.GetByNameAsync("Mistborn"));
        Assert.IsNotNull(await _repository.GetByNameAsync("Mistborn Saga"));
    }

    [TestMethod]
    public async Task RenameAsync_UnknownSeries_ThrowsKeyNotFound()
    {
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.RenameAsync("No Such Series", "New Name"));
    }

    // --- DeleteIfEmptyAsync (the atomic rollback for a failed series-mapping create) ---

    [TestMethod]
    public async Task DeleteIfEmptyAsync_DeletesAnUnmatchedShellRow()
    {
        var series = await _repository.GetOrCreateByNameAsync("Lonely Unmatched");

        var deleted = await _repository.DeleteIfEmptyAsync(series.Series.Id);

        Assert.IsTrue(deleted);
        Assert.IsNull(await _repository.GetByNameAsync("Lonely Unmatched"));
    }

    [TestMethod]
    public async Task DeleteIfEmptyAsync_RefusesWhenARowOwnsMappings()
    {
        var series = await _repository.GetOrCreateByNameAsync("Mapped Series");
        _db.SeriesMappings.Add(new SeriesMapping(default, "^mapped.*$", false, series.Series.Id));
        await _db.SaveChangesAsync();

        var deleted = await _repository.DeleteIfEmptyAsync(series.Series.Id);

        Assert.IsFalse(deleted);
        Assert.IsNotNull(await _repository.GetByNameAsync("Mapped Series"));
    }

    [TestMethod]
    public async Task DeleteIfEmptyAsync_RefusesWhenTheRowIsMatched()
    {
        var series = await _repository.UpsertSeriesAsync(new Series
        {
            Name = "Matched Series",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "123",
        });

        var deleted = await _repository.DeleteIfEmptyAsync(series.Id);

        Assert.IsFalse(deleted);
        Assert.IsNotNull(await _repository.GetByNameAsync("Matched Series"));
    }

    [TestMethod]
    public async Task DeleteIfEmptyAsync_RefusesWhenTheRowHasARoster()
    {
        var series = await _repository.GetOrCreateByNameAsync("Rostered Series");
        var now = DateTime.UtcNow;
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            Title = "The Only Book",
            SeriesPosition = "1",
            SeriesId = series.Series.Id,
            FirstSeenAt = now,
            LastRefreshedAt = now,
        });
        await _db.SaveChangesAsync();

        var deleted = await _repository.DeleteIfEmptyAsync(series.Series.Id);

        Assert.IsFalse(deleted);
        Assert.IsNotNull(await _repository.GetByNameAsync("Rostered Series"));
    }

    [TestMethod]
    public async Task DeleteIfEmptyAsync_RefusesWhenTheOmnibusFlagWasToggled()
    {
        var series = await _repository.SetIncludeOmnibusEditionsAsync("Toggled Series", true);

        var deleted = await _repository.DeleteIfEmptyAsync(series.Id);

        Assert.IsFalse(deleted);
        Assert.IsNotNull(await _repository.GetByNameAsync("Toggled Series"));
    }

    // Regression for the review finding: the rollback path used to DeleteAsync unconditionally,
    // so a request that lost the create race but had inserted its own (valid) pattern onto the
    // just-created row in the meantime had its pattern cascaded away. The conditional delete must
    // see that mapping - written by another context, as a real concurrent request would be - and
    // refuse to delete the row.
    [TestMethod]
    public async Task DeleteIfEmptyAsync_SeesAMappingAConcurrentContextWroteAndRefusesToDelete()
    {
        var series = (await _repository.GetOrCreateByNameAsync("Race Series")).Series;

        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        using (var concurrent = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings))
        {
            concurrent.SeriesMappings.Add(new SeriesMapping(default, "^racer.*$", false, series.Id));
            await concurrent.SaveChangesAsync();
        }

        var deleted = await _repository.DeleteIfEmptyAsync(series.Id);

        Assert.IsFalse(deleted, "a row another request mapped onto must survive the rollback");
        Assert.IsNotNull(await _repository.GetByNameAsync("Race Series"));
        Assert.AreEqual(1, await _db.SeriesMappings.AsNoTracking().CountAsync(m => m.SeriesId == series.Id),
            "the concurrent mapping must not have been cascaded away");
    }

    [TestMethod]
    public async Task DeleteSeriesAsync_UnlinksAndDeletesOrphanExpectedBooksAndCascadesMappings()
    {
        var series = await SeedSeriesAsync();
        _db.SeriesMappings.Add(new SeriesMapping(default, "^mistborn.*$", false, series.Id));
        // A book the author rosters also report must survive the series deletion as an
        // author-linked row (expected_books.series_id is SET NULL, not cascading).
        var person = new Person(default, "Brandon Sanderson");
        _db.Persons.Add(person);
        await _db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        var authorLinked = new ExpectedBook
        {
            SourceName = "Hardcover",
            Title = "The Way of Kings",
            SeriesPosition = "1",
            SeriesId = series.Id,
            FirstSeenAt = now,
            LastRefreshedAt = now,
            AuthorLinks = new List<ExpectedBookAuthor>
            {
                new() { PersonId = person.Id, AuthorName = "Brandon Sanderson" },
            },
        };
        _db.ExpectedBooks.Add(authorLinked);
        await _db.SaveChangesAsync();

        var deleted = await _repository.DeleteSeriesAsync("Mistborn");

        Assert.IsTrue(deleted);
        Assert.IsNull(await _repository.GetByNameAsync("Mistborn"));
        Assert.AreEqual(0, await _db.ExpectedBooks.AsNoTracking()
                .CountAsync(b => b.Id == authorLinked.Id && b.SeriesId != null),
            "the series' expected books must not keep their (now deleted) series link");
        var surviving = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).SingleAsync();
        Assert.AreEqual(authorLinked.Id, surviving.Id,
            "the author-linked book survives the series deletion");
        Assert.IsNull(surviving.SeriesId, "its series link is cleared, but the row is not deleted");
        Assert.AreEqual(1, surviving.AuthorLinks.Count);
        Assert.AreEqual(0, await _db.ExpectedBooks.AsNoTracking()
                .CountAsync(b => b.SeriesId == series.Id),
            "no series-linked expected book may survive");
        Assert.AreEqual(0, await _db.SeriesMappings.AsNoTracking().CountAsync(m => m.SeriesId == series.Id),
            "the mapping patterns must cascade away with the series row");
    }

    [TestMethod]
    public async Task DeleteSeriesAsync_NoRowForTheName_ReturnsFalse()
    {
        var deleted = await _repository.DeleteSeriesAsync("Does Not Exist");

        Assert.IsFalse(deleted);
    }

    [TestMethod]
    public async Task GetByMatchedSourceIdAsync_ReturnsTheRowMatchingBothSourceNameAndSourceId()
    {
        await _repository.UpsertSeriesAsync(new Series
        {
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
        });

        var result = await _repository.GetByMatchedSourceIdAsync("Hardcover", "42");

        Assert.IsNotNull(result);
        Assert.AreEqual("Mistborn", result.Name);
    }

    [TestMethod]
    public async Task GetByMatchedSourceIdAsync_SameIdDifferentSource_DoesNotMatch()
    {
        // The composite key is (source name, source id) together - a source id that happens to
        // collide with a different source's id must not resolve to the wrong series.
        await _repository.UpsertSeriesAsync(new Series
        {
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
        });

        var result = await _repository.GetByMatchedSourceIdAsync("SomeOtherSource", "42");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetByMatchedSourceIdAsync_UnmatchedSeries_ReturnsNull()
    {
        await _repository.GetOrCreateByNameAsync("Unmatched Series");

        var result = await _repository.GetByMatchedSourceIdAsync("Hardcover", "42");

        Assert.IsNull(result);
    }

    /// <summary>
    /// The AsNoTracking roster of a series - the read shape the app itself uses after a set-based
    /// mutation, since the change tracker is bypassed and a tracked re-load of the collection in
    /// the SAME context would echo the pre-update flag (or, after the update's detach, duplicate
    /// the row). The repository's own ignore tests had to switch to this read for the same reason
    /// the set-based mutators document their DetachTracked.
    /// </summary>
    private async Task<Series> GetRosterViewAsync(string name)
    {
        var (row, _) = await _repository.GetByNameWithExpectedBooksBoundedAsync(name, maxExpectedBooks: 10);
        return row!;
    }
}
