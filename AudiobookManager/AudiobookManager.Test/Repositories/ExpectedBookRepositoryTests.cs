using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class ExpectedBookRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private ExpectedBookRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"expectedbookrepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new ExpectedBookRepository(_db);
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

    private async Task<long> SeedPersonAsync(string name = "Brandon Sanderson")
    {
        var person = new Person(default, name);
        _db.Persons.Add(person);
        await _db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<Series> SeedSeriesAsync(string name = "The Stormlight Archive", string? sourceId = "hc-series-1")
    {
        var series = new Series
        {
            Name = name,
            MatchedSourceName = "Hardcover",
            MatchedSourceId = sourceId,
            MatchedSeriesName = "The Stormlight Archive",
        };
        _db.Series.Add(series);
        await _db.SaveChangesAsync();
        return series;
    }

    private static ExpectedBookUpsert MakeUpsert(
        string sourceBookId = "hc-1",
        string title = "The Way of Kings",
        int? year = 2010,
        DateOnly? releaseDate = null,
        string? imageUrl = null,
        long? seriesId = null,
        string? sourceSeriesId = null,
        string? sourceSeriesName = "The Stormlight Archive",
        string? seriesPosition = null,
        IReadOnlyList<ExpectedBookAuthorLink>? authors = null,
        // Series-shaped by default (the helper feeds the series refresh shape most tests are
        // about); an author-shaped poll passes null explicitly.
        bool? isCompilation = false,
        string? sourceUrl = null) => new(
            "Hardcover",
            sourceBookId,
            title,
            year,
            releaseDate,
            sourceUrl,
            imageUrl,
            seriesId,
            sourceSeriesId,
            sourceSeriesName,
            seriesPosition,
            isCompilation,
            authors ?? new List<ExpectedBookAuthorLink>());

    private async Task<long> InsertBookRowAsync(
        string title,
        long? personId = null,
        bool isIgnored = false)
    {
        var now = DateTime.UtcNow;
        var book = new ExpectedBook
        {
            SourceName = "Hardcover",
            Title = title,
            IsIgnored = isIgnored,
            FirstSeenAt = now,
            LastRefreshedAt = now,
        };
        if (personId is not null)
        {
            var person = await _db.Persons.FindAsync(personId);
            book.AuthorLinks.Add(new ExpectedBookAuthor
            {
                PersonId = personId,
                AuthorName = person!.Name,
            });
        }
        _db.ExpectedBooks.Add(book);
        await _db.SaveChangesAsync();
        return book.Id;
    }

    [TestMethod]
    public async Task UpsertAsync_NewBook_InsertsWithAuthorLinkAndFirstSeenTimestamp()
    {
        var personId = await SeedPersonAsync();
        var before = DateTime.UtcNow;

        var id = await _repository.UpsertAsync(MakeUpsert(authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).SingleAsync();
        Assert.AreEqual(id, stored.Id, "UpsertAsync returns the stored row's id");
        Assert.AreEqual("The Way of Kings", stored.Title);
        Assert.AreEqual("hc-1", stored.SourceBookId);
        Assert.AreEqual(1, stored.AuthorLinks.Count);
        Assert.AreEqual(personId, stored.AuthorLinks.Single().PersonId);
        Assert.AreEqual("Brandon Sanderson", stored.AuthorLinks.Single().AuthorName);
        Assert.IsTrue(stored.FirstSeenAt >= before, "FirstSeenAt must be stamped when the row is inserted");
        Assert.IsTrue(stored.FirstSeenAt <= DateTime.UtcNow, "FirstSeenAt must never be in the future");
        Assert.IsFalse(stored.IsIgnored);
    }

    // Regression guard: the same (source_name, source_book_id) discovered by a later poll must
    // refresh the existing row in place - one row, corrected fields, and no second author link.
    [TestMethod]
    public async Task UpsertAsync_SameSourceAndBookIdTwice_RefreshesOneRowWithoutDuplicatingLinks()
    {
        var personId = await SeedPersonAsync();
        var first = await _repository.UpsertAsync(MakeUpsert(title: "Untitled Sanderson Novel", authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));
        var second = await _repository.UpsertAsync(MakeUpsert(title: "The Way of Kings", year: 2010, authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        Assert.AreEqual(first, second, "a refresh of the same source book returns the same row id");
        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).ToListAsync();
        Assert.AreEqual(1, stored.Count);
        Assert.AreEqual("The Way of Kings", stored.Single().Title);
        Assert.AreEqual(1, stored.Single().AuthorLinks.Count);
    }

    [TestMethod]
    public async Task UpsertAsync_ConcurrentFirstWrites_AllSucceedAndCreateOneRow()
    {
        var personId = await SeedPersonAsync();
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new ExpectedBookRepository(context);
                calls.Add(Task.Run(() => repository.UpsertAsync(
                    MakeUpsert(authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }))));
            }

            await Task.WhenAll(calls);

            var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).ToListAsync();
            Assert.AreEqual(1, stored.Count, "Every concurrent first-write must converge on a single row, not fail or duplicate.");
            Assert.AreEqual(1, stored.Single().AuthorLinks.Count);
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
    public async Task UpsertManyAsync_ReturnsOneStoredIdPerInput_AndRefreshesInPlaceOnARedo()
    {
        var series = await SeedSeriesAsync();
        var personId = await SeedPersonAsync();

        var ids = await _repository.UpsertManyAsync(new List<ExpectedBookUpsert>
        {
            MakeUpsert(sourceBookId: "hc-1", title: "The Way of Kings", seriesId: series.Id, seriesPosition: "1"),
            MakeUpsert(
                sourceBookId: "hc-2",
                title: "Words of Radiance",
                seriesId: series.Id,
                seriesPosition: "2",
                authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }),
        });

        Assert.AreEqual(2, ids.Count);
        var stored = await _db.ExpectedBooks.AsNoTracking().ToListAsync();
        Assert.AreEqual(2, stored.Count);
        foreach (var id in ids)
        {
            Assert.IsNotNull(stored.FirstOrDefault(b => b.Id == id),
                "each returned id is a stored row's id");
        }
        Assert.IsTrue(stored.Single(b => b.Title == "The Way of Kings").SeriesId == series.Id);

        // A second batch over the same natural keys refreshes the same rows in place - one row
        // per book id, same ids back.
        var redone = await _repository.UpsertManyAsync(new List<ExpectedBookUpsert>
        {
            MakeUpsert(sourceBookId: "hc-1", title: "The Way of Kings (Revised)", seriesId: series.Id, seriesPosition: "1"),
        });
        Assert.AreEqual(1, redone.Count);
        Assert.AreEqual(ids[0], redone[0], "a re-upsert returns the same row, not a duplicate");
        Assert.AreEqual(2, await _db.ExpectedBooks.AsNoTracking().CountAsync());
        Assert.IsNotNull(await _db.ExpectedBooks.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == redone[0] && b.Title == "The Way of Kings (Revised)"));
    }

    // Regression guard for the adoption path: a row copied from the legacy tables carries a
    // synthetic 'legacy-author:' source id (not null - see the migration's hand-edited copy),
    // and stays that way until a refresh with a real source id matches it by natural key (same
    // author link + normalized title). The adoption must happen in place - the ignore decision
    // the user made on the copied row must survive the refresh.
    [TestMethod]
    public async Task UpsertAsync_AdoptsLegacyAuthorRowByNaturalKey_SetsTheSourceBookIdAndPreservesIgnored()
    {
        var personId = await SeedPersonAsync();

        var now = DateTime.UtcNow;
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            SourceBookId = ExpectedBook.LegacyAuthorSyntheticPrefix + "7",
            Title = "The Way of Kings",
            Year = 2009,
            IsIgnored = true,
            FirstSeenAt = now,
            LastRefreshedAt = now,
            AuthorLinks = new List<ExpectedBookAuthor>
            {
                new() { PersonId = personId, AuthorName = "Brandon Sanderson" },
            },
        });
        await _db.SaveChangesAsync();

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-123",
            title: "The Way of Kings",
            year: 2010,
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).SingleAsync();
        Assert.AreEqual("hc-123", stored.SourceBookId,
            "the synthetic migrated id must be overwritten with the real source book id");
        Assert.AreEqual("The Way of Kings", stored.Title);
        Assert.AreEqual(2010, stored.Year, "the adopted row must be refreshed from the poll");
        Assert.IsTrue(stored.IsIgnored, "adoption must preserve the user's ignore decision");
        Assert.AreEqual(1, stored.AuthorLinks.Count);
    }

    [TestMethod]
    public async Task UpsertAsync_AdoptsLegacySeriesRowByNaturalKey_WhenSeriesAndTitleAndPositionMatch()
    {
        var series = await SeedSeriesAsync();

        var now = DateTime.UtcNow;
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            SourceBookId = ExpectedBook.LegacySeriesSyntheticPrefix + "3",
            Title = "The Way of Kings",
            SeriesId = series.Id,
            SourceSeriesId = series.MatchedSourceId,
            SourceSeriesName = "The Stormlight Archive",
            SeriesPosition = "1",
            IsCompilation = true,
            FirstSeenAt = now,
            LastRefreshedAt = now,
        });
        await _db.SaveChangesAsync();

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-456",
            title: "The Way of Kings",
            seriesId: series.Id,
            sourceSeriesId: series.MatchedSourceId,
            seriesPosition: "1",
            isCompilation: false));

        var stored = await _db.ExpectedBooks.AsNoTracking().SingleAsync();
        Assert.AreEqual("hc-456", stored.SourceBookId,
            "the synthetic migrated id must be overwritten with the real source book id");
        Assert.AreEqual("The Way of Kings", stored.Title);
        Assert.AreEqual(series.Id, stored.SeriesId);
        Assert.IsFalse(stored.IsCompilation);
    }

    // Behavior guard: a natural-key adoption may only ever touch a row whose source id is a
    // synthetic legacy id (or absent) - a row a refresh has already adopted, or one a real poll
    // inserted with its real id, has settled identity. Matching it by natural key and
    // overwriting the id would let an unrelated book steal it. This behaviour held before the
    // synthetic-id fix too (the legacy filter matched only null ids), so it is a guard against
    // a future widening, not a red-on-old-code regression test.
    [TestMethod]
    public async Task UpsertAsync_DoesNotAdoptARowWhoseSourceBookIdIsAlreadyReal()
    {
        var personId = await SeedPersonAsync();

        var now = DateTime.UtcNow;
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            SourceBookId = "hc-999",
            Title = "The Way of Kings",
            IsIgnored = true,
            FirstSeenAt = now,
            LastRefreshedAt = now,
            AuthorLinks = new List<ExpectedBookAuthor>
            {
                new() { PersonId = personId, AuthorName = "Brandon Sanderson" },
            },
        });
        await _db.SaveChangesAsync();

        // Same author, same title - every natural key matches - but the poll carries a different
        // real id, so it cannot be the same source book.
        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1000",
            title: "The Way of Kings",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).ToListAsync();
        Assert.AreEqual(2, stored.Count, "a real-id row is never adopted by natural key");
        Assert.AreEqual("hc-999", stored.Single(b => b.SourceBookId == "hc-999").SourceBookId,
            "the real stored id must never be overwritten");
        Assert.AreEqual("hc-1000", stored.Single(b => b.SourceBookId == "hc-1000").SourceBookId);
    }

    // Regression guard: ImageUrl is the one refreshed field that is legitimately nullable per
    // poll (a transiently-missing cached_image) - same rule as
    // UpcomingReleaseRepository.ApplyRefresh.
    [TestMethod]
    public async Task UpsertAsync_ExistingBook_KeepsThePreviousImageWhenTheNewPollHasNone()
    {
        await _repository.UpsertAsync(MakeUpsert(imageUrl: "https://covers.hardcover.app/wok.jpg"));

        await _repository.UpsertAsync(MakeUpsert(title: "The Way of Kings (Revised)", imageUrl: null));

        var stored = await _db.ExpectedBooks.AsNoTracking().SingleAsync();
        Assert.AreEqual("The Way of Kings (Revised)", stored.Title);
        Assert.AreEqual("https://covers.hardcover.app/wok.jpg", stored.ImageUrl);
    }

    // Regression guard: a stored link that already has a PersonId must not be duplicated by a
    // later poll that only carries the author name (the always-stored fallback match key).
    [TestMethod]
    public async Task UpsertAsync_ExistingPersonLink_MatchedByNameWhenTheNewPollHasNoPersonId()
    {
        var personId = await SeedPersonAsync();
        await _repository.UpsertAsync(MakeUpsert(authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        await _repository.UpsertAsync(MakeUpsert(title: "The Way of Kings", authors: new[] { new ExpectedBookAuthorLink(null, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).SingleAsync();
        Assert.AreEqual(1, stored.AuthorLinks.Count);
        Assert.AreEqual(personId, stored.AuthorLinks.Single().PersonId);
    }

    // Regression guard (M1): a link stored name-only - a poll that earlier could not resolve the
    // author to a person - must be upgraded to the PersonId on the later poll that does resolve
    // it, not duplicated beside itself as a person-linked twin.
    [TestMethod]
    public async Task UpsertAsync_UpgradesANameOnlyAuthorLinkWhenThePollResolvesItsPerson()
    {
        var personId = await SeedPersonAsync();
        await _repository.UpsertAsync(MakeUpsert(authors: new[] { new ExpectedBookAuthorLink(null, "Brandon Sanderson") }));

        await _repository.UpsertAsync(MakeUpsert(title: "The Way of Kings", authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).SingleAsync();
        Assert.AreEqual(1, stored.AuthorLinks.Count, "resolving the author must not add a second link");
        Assert.AreEqual(personId, stored.AuthorLinks.Single().PersonId,
            "the name-only link must be upgraded in place");
        Assert.AreEqual("Brandon Sanderson", stored.AuthorLinks.Single().AuthorName);
    }

    // Regression guard (H2): the same book is discovered by an author refresh (no series identity
    // at all) and by a series refresh (catalog + source series identity). The author-shaped poll
    // must refresh ordinary fields without clearing the series placement the series-shaped one
    // established - only UnlinkSeriesBooksAsync may drop a series link.
    [TestMethod]
    public async Task UpsertAsync_AuthorShapedRefresh_KeepsAnExistingSeriesLinkAndPosition()
    {
        var personId = await SeedPersonAsync();
        var series = await SeedSeriesAsync();

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1",
            title: "The Way of Kings",
            seriesId: series.Id,
            sourceSeriesId: series.MatchedSourceId,
            sourceSeriesName: "The Stormlight Archive",
            seriesPosition: "1",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1",
            title: "The Way of Kings (Revised Title)",
            year: 2010,
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().SingleAsync();
        Assert.AreEqual("The Way of Kings (Revised Title)", stored.Title,
            "ordinary fields must still be refreshed");
        Assert.AreEqual(2010, stored.Year);
        Assert.AreEqual(series.Id, stored.SeriesId, "the catalog series link survives an author-shaped refresh");
        Assert.AreEqual(series.MatchedSourceId, stored.SourceSeriesId);
        Assert.AreEqual("The Stormlight Archive", stored.SourceSeriesName);
        Assert.AreEqual("1", stored.SeriesPosition, "the series placement survives an author-shaped refresh");
    }

    // Regression guard (author-series review finding H2): an author-shaped poll must not clear
    // IsCompilation set by a series-shaped poll on the SAME shared row - the bibliography feed
    // reports no compilation data, so the author refresh passes null and the stored flag survives.
    [TestMethod]
    public async Task UpsertAsync_AuthorShapedRefresh_PreservesTheCompilationFlagASeriesPollSet()
    {
        var personId = await SeedPersonAsync();
        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1",
            title: "The Way of Kings",
            isCompilation: true));

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1",
            title: "The Way of Kings (Revised Title)",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") },
            isCompilation: null));

        var stored = await _db.ExpectedBooks.AsNoTracking().SingleAsync();
        Assert.AreEqual("The Way of Kings (Revised Title)", stored.Title,
            "ordinary fields must still be refreshed by the author-shaped poll");
        Assert.IsTrue(stored.IsCompilation, "an author-shaped poll carries no compilation data and must not clear the series' flag");
    }

    // The opposite direction of the same rule: a poll that genuinely says a book is (or is not) a
    // compilation overwrites the stored flag - the nullability is about "the poll doesn't know",
    // not about refusing to change.
    [TestMethod]
    public async Task UpsertAsync_SeriesShapedPollWithACompilationFlag_OverwritesTheStoredFlag()
    {
        await _repository.UpsertAsync(MakeUpsert(sourceBookId: "hc-1", title: "The Way of Kings", isCompilation: false));

        await _repository.UpsertAsync(MakeUpsert(sourceBookId: "hc-1", title: "The Way of Kings", isCompilation: true));

        var stored = await _db.ExpectedBooks.AsNoTracking().SingleAsync();
        Assert.IsTrue(stored.IsCompilation, "a poll that carries an explicit compilation flag is authoritative");
    }

    // Regression guard (M4): legacy natural-key adoption matches titles accent-insensitively and
    // case-insensitively, in either direction - the same folding the fold_accents SQL function
    // applies to search, so an accented stored title is found by an unaccented poll title.
    [TestMethod]
    public async Task UpsertAsync_AdoptsLegacyRow_MatchingAccentedAndCaseVaryingTitles()
    {
        var personId = await SeedPersonAsync();

        var now = DateTime.UtcNow;
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            SourceBookId = ExpectedBook.LegacyAuthorSyntheticPrefix + "9",
            Title = "Maîtres du Monde",
            FirstSeenAt = now,
            LastRefreshedAt = now,
            IsIgnored = true,
            AuthorLinks = new List<ExpectedBookAuthor>
            {
                new() { PersonId = personId, AuthorName = "Brandon Sanderson" },
            },
        });
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            SourceBookId = ExpectedBook.LegacyAuthorSyntheticPrefix + "10",
            Title = "Le Coeur de Lune",
            FirstSeenAt = now,
            LastRefreshedAt = now,
            AuthorLinks = new List<ExpectedBookAuthor>
            {
                new() { PersonId = personId, AuthorName = "Brandon Sanderson" },
            },
        });
        await _db.SaveChangesAsync();

        // Accented stored title matched by an unaccented, differently-cased poll title...
        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-900",
            title: "MAITRES DU MONDE",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));
        // ...and a plain stored title matched by an accented, differently-cased poll title.
        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-901",
            title: "LE COEUR DE LÙNE",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).ToListAsync();
        Assert.AreEqual(2, stored.Count, "both polls must adopt the copy rows, never insert duplicates");
        Assert.IsFalse(stored.Any(b => b.SourceBookId?.StartsWith(ExpectedBook.LegacyAuthorSyntheticPrefix) == true),
            "every synthetic copy id must have been overwritten by the adopting poll, never duplicated alongside");
        var accentedPoll = stored.Single(b => b.SourceBookId == "hc-900");
        Assert.IsTrue(accentedPoll.IsIgnored,
            "ignore decisions survive the accent-insensitive adoption too");
        Assert.AreEqual("MAITRES DU MONDE", accentedPoll.Title,
            "the adopted row is refreshed from the poll");
        Assert.IsTrue(stored.Any(b => b.SourceBookId == "hc-901"),
            "the reverse direction (accented poll, plain stored title) adopts too");
    }

    [TestMethod]
    public async Task PruneAuthorLinksThenDeleteOrphan_DeletesAnUnlinkedSerieslessBookButKeepsLinkedOnes()
    {
        var personId = await SeedPersonAsync();
        var otherPersonId = await SeedPersonAsync("Robert Jordan");
        var series = await SeedSeriesAsync();

        var standaloneId = await InsertBookRowAsync("Elantris", personId: personId);
        var seriesBookId = await InsertBookRowAsync("The Way of Kings", personId: personId);
        var linked = _db.ExpectedBooks.Find(seriesBookId)!;
        linked.SeriesId = series.Id;
        linked.SourceSeriesId = series.MatchedSourceId;
        var otherAuthorBookId = await InsertBookRowAsync("The Eye of the World", personId: otherPersonId);
        await _db.SaveChangesAsync();

        // The person no longer owns the standalone book but still owns the series book.
        await _repository.PruneAuthorLinksAsync(personId, new[] { seriesBookId });
        await _repository.DeleteOrphanExpectedBooksAsync();

        var remaining = await _db.ExpectedBooks.AsNoTracking().Select(b => b.Id).ToListAsync();
        Assert.AreEqual(2, remaining.Count);
        Assert.IsTrue(remaining.Contains(seriesBookId), "the series-linked book must be kept");
        Assert.IsTrue(remaining.Contains(otherAuthorBookId), "a book linked to another author must be kept");
        Assert.IsFalse(remaining.Contains(standaloneId), "the unlinked, series-less book must be deleted");
    }

    // Regression guard for the review finding: a keep-list larger than MaxInClauseIdsPerQuery
    // used to become ONE unchunked NOT IN clause. The keep lookups are chunked now - this test
    // drives a 1,200+-id keep-list through the method, asserting the resulting prune still keeps
    // exactly the listed books' links (and drops the rest), which is what the chunked
    // merge-then-filter must produce.
    [TestMethod]
    public async Task PruneAuthorLinksAsync_KeepListLargerThanTheChunkSize_KeepsOnlyTheListedLinks()
    {
        var personId = await SeedPersonAsync();
        var keptIds = new List<long>();
        var prunedIds = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            keptIds.Add(await InsertBookRowAsync($"Keep {i}", personId: personId));
        }
        for (var i = 0; i < 10; i++)
        {
            prunedIds.Add(await InsertBookRowAsync($"Prune {i}", personId: personId));
        }

        // 1,200 ids starting at 1000 cannot collide with the row ids the seeds just consumed,
        // so only the three real kept ids are meaningful members; the list still crosses the
        // 500-id chunk size (three chunks).
        var keepBookIds = Enumerable.Range(1000, 1200).Select(i => (long)i).ToList();
        keepBookIds.AddRange(keptIds);

        await _repository.PruneAuthorLinksAsync(personId, keepBookIds);

        var remainingBookIds = await _db.ExpectedBookAuthors.AsNoTracking()
            .Where(l => l.PersonId == personId)
            .OrderBy(l => l.ExpectedBookId)
            .Select(l => l.ExpectedBookId)
            .ToListAsync();
        Assert.AreSequenceEqual(keptIds, remainingBookIds,
            "only the keep-listed books' links survive the prune, in id order");
    }

    [TestMethod]
    public async Task UnlinkSeriesBooksAsync_KeepsAuthorLinkedRowsAndKeepsTheSourceSeriesFields()
    {
        var personId = await SeedPersonAsync();
        var series = await SeedSeriesAsync();

        var authorLinked = await InsertBookRowAsync("The Way of Kings", personId: personId);
        var linkless = await InsertBookRowAsync("Words of Radiance");
        foreach (var id in new[] { authorLinked, linkless })
        {
            _db.ExpectedBooks.Find(id)!.SeriesId = series.Id;
            _db.ExpectedBooks.Find(id)!.SourceSeriesId = series.MatchedSourceId;
            _db.ExpectedBooks.Find(id)!.SourceSeriesName = "The Stormlight Archive";
            _db.ExpectedBooks.Find(id)!.SeriesPosition = id == authorLinked ? "1" : "2";
        }
        await _db.SaveChangesAsync();

        // A keep-list with just the author-linked book: the linkless book is unlinked but never
        // deleted - the row survives because a future author refresh can still attribute it.
        await _repository.UnlinkSeriesBooksAsync(series.Id, new[] { authorLinked });

        var kept = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).ToListAsync();
        Assert.AreEqual(2, kept.Count, "unlinking must never delete an author-linked row");
        var linklessRow = kept.Single(b => b.Id == linkless);
        Assert.IsNull(linklessRow.SeriesId);
        Assert.AreEqual("The Stormlight Archive", linklessRow.SourceSeriesName,
            "the source-series fields record what the source reported and are kept");
        Assert.IsNotNull(kept.Single(b => b.Id == authorLinked).SeriesId);
    }

    // Regression guard for the review finding: same unchunked-IN hazard as
    // PruneAuthorLinksAsync, on the update side - a keep-list crossing the chunk size must leave
    // exactly the listed books linked and clear the rest, with the lookup chunked so no single
    // query carries the whole list.
    [TestMethod]
    public async Task UnlinkSeriesBooksAsync_KeepListLargerThanTheChunkSize_KeepsOnlyTheListedBooksLinked()
    {
        var series = await SeedSeriesAsync();
        var keptIds = new List<long>();
        var unlinkedIds = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            keptIds.Add(await InsertBookRowAsync($"Keep {i}"));
        }
        for (var i = 0; i < 10; i++)
        {
            unlinkedIds.Add(await InsertBookRowAsync($"Unlink {i}"));
        }
        foreach (var id in keptIds.Concat(unlinkedIds))
        {
            _db.ExpectedBooks.Find(id)!.SeriesId = series.Id;
            _db.ExpectedBooks.Find(id)!.SourceSeriesId = series.MatchedSourceId;
            _db.ExpectedBooks.Find(id)!.SourceSeriesName = "The Stormlight Archive";
        }
        await _db.SaveChangesAsync();

        // 1,200 filler ids (starting beyond any row id) plus the three real kept ids: three
        // chunks through the keep lookups.
        var keepBookIds = Enumerable.Range(1000, 1200).Select(i => (long)i).ToList();
        keepBookIds.AddRange(keptIds);

        await _repository.UnlinkSeriesBooksAsync(series.Id, keepBookIds);

        var linkedIds = await _db.ExpectedBooks.AsNoTracking()
            .Where(b => b.SeriesId == series.Id)
            .OrderBy(b => b.Id)
            .Select(b => b.Id)
            .ToListAsync();
        Assert.AreSequenceEqual(keptIds, linkedIds,
            "only the keep-listed books stay linked to the series, in id order");
        foreach (var id in unlinkedIds)
        {
            Assert.IsNull(_db.ExpectedBooks.AsNoTracking().Single(b => b.Id == id).SeriesId,
                "a book outside the keep-list must lose its series link");
        }
    }

    [TestMethod]
    public async Task GetByAuthorBoundedAsync_ReturnsOnlyThatAuthorsBooks_WithAnOverflowFlag()
    {
        var personId = await SeedPersonAsync();
        var otherPersonId = await SeedPersonAsync("Robert Jordan");
        var ownId = await InsertBookRowAsync("Elantris", personId: personId);
        await InsertBookRowAsync("The Eye of the World", personId: otherPersonId);

        var (items, overflow) = await _repository.GetByAuthorBoundedAsync(personId, 10);

        Assert.IsFalse(overflow);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(ownId, items.Single().Id);
    }

    [TestMethod]
    public async Task GetByAuthorBoundedAsync_MoreRowsThanTheCap_ReportsOverflowAndTrims()
    {
        var personId = await SeedPersonAsync();
        for (var i = 0; i < 3; i++)
        {
            await InsertBookRowAsync($"Book {i}", personId: personId);
        }

        var (items, overflow) = await _repository.GetByAuthorBoundedAsync(personId, 2);

        Assert.AreEqual(2, items.Count, "the fetch is bounded to the cap, never the whole list");
        Assert.IsTrue(overflow);
    }

    [TestMethod]
    public async Task GetBySeriesBoundedAsync_ReturnsOnlyThatSeriesBooks_WithAnOverflowFlag()
    {
        var personId = await SeedPersonAsync();
        var series = await SeedSeriesAsync();
        var otherSeries = await SeedSeriesAsync("Mistborn", "hc-series-2");

        var bookId = await InsertBookRowAsync("The Way of Kings", personId: personId);
        var otherId = await InsertBookRowAsync("The Final Empire", personId: personId);
        _db.ExpectedBooks.FindAsync(bookId).Result!.SeriesId = series.Id;
        _db.ExpectedBooks.FindAsync(otherId).Result!.SeriesId = otherSeries.Id;
        await _db.SaveChangesAsync();

        var (items, overflow) = await _repository.GetBySeriesBoundedAsync(series.Id, 10);

        Assert.IsFalse(overflow);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(bookId, items.Single().Id);
    }

    [TestMethod]
    public async Task SetIgnoredAsync_RoundTripsTheFlag()
    {
        var id = await InsertBookRowAsync("Elantris");

        await _repository.SetIgnoredAsync(id, true);
        Assert.IsTrue((await _repository.GetByIdAsync(id))!.IsIgnored);

        await _repository.SetIgnoredAsync(id, false);
        Assert.IsFalse((await _repository.GetByIdAsync(id))!.IsIgnored);
    }

    [TestMethod]
    public async Task SetIgnoredByIdAsync_SetsTheFlagOnTheExactRow()
    {
        var personId = await SeedPersonAsync();
        var firstId = await InsertBookRowAsync("Elantris", personId: personId);
        var secondId = await InsertBookRowAsync("Elantris", personId: personId);

        await _repository.SetIgnoredByIdAsync(firstId, true);

        Assert.IsTrue((await _repository.GetByIdAsync(firstId))!.IsIgnored);
        Assert.IsFalse((await _repository.GetByIdAsync(secondId))!.IsIgnored,
            "each same-titled row is addressed independently by its stable id - the ambiguity the title path has");
    }

    // The author detail page's ignore action addresses the SHARED unified row (the same row a
    // series refresh or another author may link to), so dismissing here hides the book from every
    // scope - global ignore. The lookup is the same natural-key rule as the legacy author table:
    // trimmed, case-insensitive title among the person's linked books.
    [TestMethod]
    public async Task SetExpectedBookIgnoredByPersonAsync_SetsTheFlagOnTheSharedRow_AndNeverTouchesOtherAuthors()
    {
        var personId = await SeedPersonAsync();
        var otherPersonId = await SeedPersonAsync("Robert Jordan");
        var ownId = await InsertBookRowAsync("  Elantris  ", personId: personId);
        var otherId = await InsertBookRowAsync("Elantris", personId: otherPersonId);

        await _repository.SetExpectedBookIgnoredByPersonAsync(personId, "ELANTRIS", true, 10);

        Assert.IsTrue((await _repository.GetByIdAsync(ownId))!.IsIgnored);
        Assert.IsFalse((await _repository.GetByIdAsync(otherId))!.IsIgnored,
            "the flag is set only on the shared row of THIS author's matching book");
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredByPersonAsync_UnknownTitle_ThrowsKeyNotFound()
    {
        var personId = await SeedPersonAsync();

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredByPersonAsync(personId, "Nonexistent", true, 10));
    }

    // Regression guard (author-series review finding M2 - legacy adoption fall-through): a poll
    // that carries a source-series identity but finds no legacy row to adopt by series must fall
    // through to the author natural key. The legacy author table had no series concept, so a
    // pre-migration dismissed row copied from it is author-linked with NO series fields at all -
    // invisible to the series-scoped branch. Without the fall-through the refresh inserts a
    // parallel row, the prune drops the copy's author link, and the orphan-delete removes the
    // copy - the user's ignore decision deleted with it.
    [TestMethod]
    public async Task UpsertAsync_AdoptsALegacyAuthorRowWhenThePollCarriesASeriesIdentity()
    {
        var personId = await SeedPersonAsync();

        var now = DateTime.UtcNow;
        _db.ExpectedBooks.Add(new ExpectedBook
        {
            SourceName = "Hardcover",
            SourceBookId = ExpectedBook.LegacyAuthorSyntheticPrefix + "7",
            Title = "The Way of Kings",
            Year = 2009,
            IsIgnored = true,
            FirstSeenAt = now,
            LastRefreshedAt = now,
            AuthorLinks = new List<ExpectedBookAuthor>
            {
                new() { PersonId = personId, AuthorName = "Brandon Sanderson" },
            },
        });
        await _db.SaveChangesAsync();

        // The poll reports the book inside an (unmatched) source series, so the series-scoped
        // branch misses the copy. The author branch must still adopt it in place.
        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-123",
            title: "The Way of Kings",
            year: 2010,
            seriesId: null,
            sourceSeriesId: "hc-series-99",
            sourceSeriesName: "The Stormlight Archive",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().Include(b => b.AuthorLinks).ToListAsync();
        Assert.AreEqual(1, stored.Count, "the poll must adopt the legacy row, never insert a duplicate");
        Assert.AreEqual("hc-123", stored.Single().SourceBookId,
            "the synthetic migrated id must be overwritten with the real source book id");
        Assert.AreEqual(2010, stored.Single().Year, "the adopted row must be refreshed from the poll");
        Assert.IsTrue(stored.Single().IsIgnored, "adoption must preserve the user's ignore decision");
        Assert.AreEqual("hc-series-99", stored.Single().SourceSeriesId,
            "the poll's source-series identity lands on the adopted row");
        Assert.AreEqual(1, stored.Single().AuthorLinks.Count);
    }

    // Regression guard (author-series review finding M3 - series-unlink oscillation): a poll
    // whose source-series identity did not resolve to a local series (SeriesId null) must not
    // clear an existing catalog link whose series IS matched to the poll's own source-series id -
    // the resolution miss is an artifact of how THIS poll resolved it (the author feed can report
    // the id under a different source name), not evidence the book left the series. Without the
    // guard the link oscillates: this poll clears it, the series' next refresh re-links it.
    [TestMethod]
    public async Task UpsertAsync_UnresolvedSeriesIdentity_DoesNotClearALinkMatchedToTheSameSourceSeries()
    {
        var personId = await SeedPersonAsync();
        var series = await SeedSeriesAsync();

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1",
            title: "The Way of Kings",
            seriesId: series.Id,
            sourceSeriesId: series.MatchedSourceId,
            sourceSeriesName: "The Stormlight Archive",
            seriesPosition: "1",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        await _repository.UpsertAsync(MakeUpsert(
            sourceBookId: "hc-1",
            title: "The Way of Kings (Revised)",
            seriesId: null,
            sourceSeriesId: series.MatchedSourceId,
            sourceSeriesName: "The Stormlight Archive",
            seriesPosition: "1",
            authors: new[] { new ExpectedBookAuthorLink(personId, "Brandon Sanderson") }));

        var stored = await _db.ExpectedBooks.AsNoTracking().SingleAsync();
        Assert.AreEqual(series.Id, stored.SeriesId,
            "the catalog link must survive: the poll's identity is the same source series the linked series is matched to");
        Assert.AreEqual(series.MatchedSourceId, stored.SourceSeriesId,
            "the poll's source-series fields are still authoritative");
    }

    // The ignore-path roster read is bounded to maxBooks + 1 rows like the sibling reads, and
    // the flag write is set-based (safe against a concurrent refresh's prune). A roster past the
    // cap degrades safely: a row the natural key resolves WITHIN the readable prefix is still
    // updated, and a title that lies beyond the prefix is reported not-found - never a guess at
    // a row outside the bounded read.
    [TestMethod]
    public async Task SetExpectedBookIgnoredByPersonAsync_RosterPastTheCap_StillTouchesTheVisibleMatch()
    {
        var personId = await SeedPersonAsync();
        var missedId = await InsertBookRowAsync("The Well of Ascension", personId: personId);
        var targetId = await InsertBookRowAsync("Elantris", personId: personId);
        var beyondId = await InsertBookRowAsync("Warbreaker", personId: personId);

        await _repository.SetExpectedBookIgnoredByPersonAsync(personId, "ELANTRIS", true, 2);

        Assert.IsTrue((await _repository.GetByIdAsync(targetId))!.IsIgnored);
        Assert.IsFalse((await _repository.GetByIdAsync(missedId))!.IsIgnored);
        Assert.IsFalse((await _repository.GetByIdAsync(beyondId))!.IsIgnored);
    }

    [TestMethod]
    public async Task SetExpectedBookIgnoredByPersonAsync_RosterPastTheCap_NotFoundWhenTheTitleLiesBeyondThePrefix()
    {
        var personId = await SeedPersonAsync();
        await InsertBookRowAsync("The Way of Kings", personId: personId);
        await InsertBookRowAsync("Words of Radiance", personId: personId);
        await InsertBookRowAsync("Oathbringer", personId: personId);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _repository.SetExpectedBookIgnoredByPersonAsync(personId, "Oathbringer", true, 2));
    }

    // The source-identity dismissal route reads the row by its dedup key (source name + source
    // book id) before setting the ignore flag by its stable id.
    [TestMethod]
    public async Task GetBySourceAsync_ReturnsTheRowCarryingThatSourceIdentity()
    {
        var id = await _repository.UpsertAsync(MakeUpsert(sourceBookId: "hc-1", title: "The Way of Kings"));
        await _repository.UpsertAsync(MakeUpsert(sourceBookId: "hc-2", title: "Words of Radiance"));

        var found = await _repository.GetBySourceAsync("Hardcover", "hc-1");

        Assert.IsNotNull(found);
        Assert.AreEqual(id, found!.Id);
        Assert.AreEqual("The Way of Kings", found!.Title);
    }

    [TestMethod]
    public async Task GetBySourceAsync_UnknownIdentity_ReturnsNull()
    {
        var result = await _repository.GetBySourceAsync("Hardcover", "nope");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_ProjectsNonIgnoredBooksJoinedToTheirLinks()
    {
        var personId = await SeedPersonAsync();
        var activeId = await InsertBookRowAsync("Elantris", personId: personId);
        var ignoredId = await InsertBookRowAsync("Warbreaker", personId: personId);
        await _repository.SetIgnoredAsync(ignoredId, true);

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(10);

        Assert.IsFalse(overflow);
        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual(personId, refs.Single().PersonId);
        var refdBookId = refs.Single().ExpectedBookId;
        Assert.AreEqual(activeId, refdBookId);
        Assert.AreEqual("Elantris", refs.Single().Title);
        Assert.IsNull(refs.Single().SeriesName, "a standalone book carries no local series name");
        Assert.IsNull(refs.Single().SourceSeriesId);
        Assert.IsNull(refs.Single().SeriesPart);
    }

    // The bulk author-filter classifier needs the refs' series context to match series-linked
    // entries against owned books correctly - a book linked to a local catalog series must carry
    // that series' NAME (not just its id), plus the source-series id and position.
    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_SeriesLinkedBookCarriesItsLocalSeriesNameAndPosition()
    {
        var personId = await SeedPersonAsync();
        var series = await SeedSeriesAsync();
        var bookId = await InsertBookRowAsync("The Way of Kings", personId: personId);
        var book = await _db.ExpectedBooks.FindAsync(bookId);
        book!.SeriesId = series.Id;
        book.SourceSeriesId = series.MatchedSourceId;
        book.SourceSeriesName = "The Stormlight Archive";
        book.SeriesPosition = "1";
        await _db.SaveChangesAsync();

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(10);

        Assert.IsFalse(overflow);
        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("The Stormlight Archive", refs.Single().SeriesName);
        Assert.AreEqual(series.MatchedSourceId, refs.Single().SourceSeriesId);
        Assert.AreEqual("1", refs.Single().SeriesPart);
    }

    // An unmatched source-series book (source series id set, no local series link) carries its
    // source-series placement so the classifier can best-effort match it by position/title.
    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_UnmatchedSourceSeriesBookCarriesItsSourcePlacement()
    {
        var personId = await SeedPersonAsync();
        var bookId = await InsertBookRowAsync("Starsight", personId: personId);
        var book = await _db.ExpectedBooks.FindAsync(bookId);
        book!.SourceSeriesId = "55";
        book.SourceSeriesName = "Skyward";
        book.SeriesPosition = "2";
        await _db.SaveChangesAsync();

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(10);

        Assert.IsFalse(overflow);
        Assert.AreEqual(1, refs.Count);
        Assert.IsNull(refs.Single().SeriesName, "no local series is matched yet");
        Assert.AreEqual("55", refs.Single().SourceSeriesId);
        Assert.AreEqual("2", refs.Single().SeriesPart);
    }

    // Regression guard (M2): the refs feed a bounded author-list classifier, so the read must
    // never transfer the whole table - cap + 1 rows and an overflow flag, like the other
    // bounded expected-book reads.
    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_MoreRowsThanTheCap_ReportsOverflowAndTrims()
    {
        var personId = await SeedPersonAsync();
        var otherPersonId = await SeedPersonAsync("Robert Jordan");
        for (var i = 0; i < 3; i++)
        {
            await InsertBookRowAsync($"Book {i}", personId: personId);
        }
        await InsertBookRowAsync("The Eye of the World", personId: otherPersonId);

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(2);

        Assert.AreEqual(2, refs.Count, "the fetch is bounded to the cap, never the whole list");
        Assert.IsTrue(overflow);
    }

    // Exact-at-cap: the overflow flag must flip only PAST the cap - a ref set that exactly fills
    // it is a complete, un-trimmed answer, not an overflow.
    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_RefsExactlyAtTheCap_ReportsNoOverflow()
    {
        var personId = await SeedPersonAsync();
        for (var i = 0; i < 3; i++)
        {
            await InsertBookRowAsync($"Book {i}", personId: personId);
        }

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(3);

        Assert.AreEqual(3, refs.Count);
        Assert.IsFalse(overflow, "exactly cap rows is not an overflow");
    }

    // Exact-cap-plus-one: one row past the cap is the minimal overflow - nothing past the cap is
    // transferred, and the flag is set.
    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_RefsAtCapPlusOne_ReportsOverflowWithOnlyCapRows()
    {
        var personId = await SeedPersonAsync();
        for (var i = 0; i < 4; i++)
        {
            await InsertBookRowAsync($"Book {i}", personId: personId);
        }

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(3);

        Assert.AreEqual(3, refs.Count, "only the cap rows are returned, never the cap+1 probe row");
        Assert.IsTrue(overflow);
    }

    // Regression guard for the review finding on SQLite IN-clause limits: the bulk classifier's
    // ref set is sized for 100k libraries, and the series-name resolution pass used to translate
    // it into ONE IN clause over every book id - past a thousand or so that exceeds SQLite's
    // compiled variable limit and the whole filter 500'd. The resolution is chunked now, so a ref
    // set larger than one chunk still resolves every series link correctly.
    [TestMethod]
    public async Task GetActiveAuthorBookRefsAsync_MoreRefsThanOneInClauseChunk_ResolvesEverySeriesName()
    {
        var personId = await SeedPersonAsync();
        var series = await SeedSeriesAsync("Mistborn", "hc-series-mistborn");

        var chunkBoundary = ExpectedBookRepository.MaxInClauseIdsPerQuery + 1;
        for (var i = 0; i < chunkBoundary; i++)
        {
            var id = await InsertBookRowAsync($"Book {i}", personId: personId);
            var book = await _db.ExpectedBooks.FindAsync(id);
            book!.SeriesId = series.Id;
        }
        await _db.SaveChangesAsync();

        var (refs, overflow) = await _repository.GetActiveAuthorBookRefsAsync(chunkBoundary);

        Assert.IsFalse(overflow);
        Assert.AreEqual(chunkBoundary, refs.Count, "a ref set spanning more than one IN-clause chunk is still read in full");
        Assert.IsTrue(refs.All(r => r.SeriesName == "Mistborn"),
            "every ref's local series name resolves across the chunked read");
    }

    // Regression guard (M5): expected_book_authors.person_id is ON DELETE SET NULL, not CASCADE -
    // a deleted Person must leave the link (and its stored AuthorName) behind so the roster stays
    // readable and a later refresh can re-resolve the person.
    [TestMethod]
    public async Task DeletingAPerson_SetsTheLinkPersonIdNullAndKeepsTheAuthorName()
    {
        var personId = await SeedPersonAsync();
        var bookId = await InsertBookRowAsync("Elantris", personId: personId);

        await _db.Persons.Where(p => p.Id == personId).ExecuteDeleteAsync();

        var link = await _db.ExpectedBookAuthors.AsNoTracking().SingleAsync();
        var linkedBookId = link.ExpectedBookId;
        Assert.AreEqual(bookId, linkedBookId, "the link survives the person deletion");
        Assert.IsNull(link.PersonId, "the person FK is SET NULL, not cascading");
        Assert.AreEqual("Brandon Sanderson", link.AuthorName,
            "the always-stored name survives so the roster entry keeps its identity");
        Assert.AreEqual(1, await _db.ExpectedBooks.AsNoTracking().CountAsync(),
            "the book itself must not be deleted with the person");
    }
}