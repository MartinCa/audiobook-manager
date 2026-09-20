using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;

namespace AudiobookManager.Test.Services;

/// <summary>
/// <see cref="AuthorReconciliationProvider"/> - the author-detail counterpart of
/// <see cref="SeriesReconciliationProvider"/> (which has no dedicated test class of its own;
/// its matching/classification is exercised indirectly through <c>SeriesServiceTests</c> and
/// <c>LibraryConsistencyServiceTests</c>). Mocks the two repositories directly rather than going
/// through SQLite - the property under test is the reconciliation's own matching/classification
/// logic over the unified expected-book rows, not the repositories' queries (which have their own
/// bounded-fetch/overflow tests, see <c>ExpectedBookRepositoryTests</c>/
/// <c>AudiobookRepositoryAuthorOwnedKeysTests</c>).
/// </summary>
[TestClass]
public class AuthorReconciliationProviderTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IExpectedBookRepository> _expectedBookRepository = null!;
    private AuthorReconciliationProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _expectedBookRepository = new Mock<IExpectedBookRepository>();
        _provider = new AuthorReconciliationProvider(_audiobookRepository.Object, _expectedBookRepository.Object);

        _audiobookRepository
            .Setup(r => r.GetOwnedKeysByAuthorAsync(It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), false));
        _audiobookRepository
            .Setup(r => r.GetOwnedKeysByAuthorsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<int>()))
            .ReturnsAsync((new List<AuthorOwnedKey>(), false));
    }

    private void SetupRoster(long personId, List<ExpectedBook> expectedBooks) =>
        _expectedBookRepository
            .Setup(r => r.GetByAuthorBoundedAsync(personId, It.IsAny<int>()))
            .ReturnsAsync((expectedBooks, false));

    private static Series? MakeSeries(long? seriesId, string? seriesName) =>
        seriesId is long id ? new Series { Id = id, Name = seriesName ?? string.Empty } : null;

    private static ExpectedBook MakeBook(
        long id, string title, int? year = null, DateOnly? releaseDate = null,
        long? seriesId = null, string? seriesName = null,
        string? sourceSeriesId = null, string? sourceSeriesName = null,
        string? position = null, bool isIgnored = false) => new()
    {
        Id = id,
        SourceName = "Hardcover",
        SourceBookId = $"sb-{id}",
        Title = title,
        Year = year,
        ReleaseDate = releaseDate,
        SeriesId = seriesId,
        Series = MakeSeries(seriesId, seriesName),
        SourceSeriesId = sourceSeriesId,
        SourceSeriesName = sourceSeriesName,
        SeriesPosition = position,
        IsIgnored = isIgnored,
        FirstSeenAt = DateTime.UtcNow,
        LastRefreshedAt = DateTime.UtcNow,
    };

    private void SetupOwned(long personId, List<SeriesOwnedKey> owned)
    {
        _audiobookRepository
            .Setup(r => r.GetOwnedKeysByAuthorAsync(personId, It.IsAny<int>()))
            .ReturnsAsync((owned, false));
    }

    private void SetupOwnedBulk(List<AuthorOwnedKey> owned)
    {
        _audiobookRepository
            .Setup(r => r.GetOwnedKeysByAuthorsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<int>()))
            .ReturnsAsync((owned, false));
    }

    [TestMethod]
    public async Task GetReconciliationAsync_UnmatchedEntryPastRelease_IsMissing()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Elantris", year: 2005),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("Elantris", result.Missing[0].Title);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(0, result.Ignored.Count);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_UnmatchedEntryWithFutureYear_IsUpcoming()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Future Novel", year: DateTime.UtcNow.Year + 1),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(1, result.Upcoming.Count);
        Assert.AreEqual("Future Novel", result.Upcoming[0].Title);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_EntryMatchingAnOwnedBook_IsNeitherMissingNorUpcoming()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Elantris", year: 2005),
        });
        SetupOwned(7, new List<SeriesOwnedKey> { new(101, null, "Elantris", null) });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(1, result.OwnedCount);
    }

    // Regression: an owned book that the library filed under a series must still satisfy a
    // standalone roster entry with the same title - the source having no series placement for a
    // book the user owns (in whatever series) must not report it missing.
    [TestMethod]
    public async Task GetReconciliationAsync_StandaloneEntry_MatchesAnOwnedBookAnySeries()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "The Way of Kings", year: 2010),
        });
        SetupOwned(7, new List<SeriesOwnedKey> { new(101, "1", "The Way of Kings", "The Stormlight Archive") });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.Missing.Count, "a book already owned must never appear missing");
        Assert.AreEqual(0, result.Upcoming.Count);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_IgnoredEntry_IsExcludedFromMissingAndUpcomingButListedInIgnored()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Elantris", year: 2005, isIgnored: true),
            MakeBook(2, "Warbreaker", year: 2009),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("Warbreaker", result.Missing[0].Title);
        Assert.AreEqual(1, result.Ignored.Count);
        Assert.AreEqual("Elantris", result.Ignored[0].Title);
        Assert.IsTrue(result.Ignored[0].IsIgnored);
        // The ignored entry contributes neither to ExpectedBookCount (which counts only active
        // roster entries) nor to Missing/Upcoming.
        Assert.AreEqual(1, result.ExpectedBookCount);
    }

    // The unified roster spans the whole bibliography: a book that belongs to a source series is
    // retained by the author refresh instead of being dropped, so the reconciliation must include
    // and classify it too.
    [TestMethod]
    public async Task GetReconciliationAsync_SeriesLinkedEntry_IsClassified()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "The Way of Kings", year: 2010,
                seriesId: 3, seriesName: "The Stormlight Archive", sourceSeriesId: "55", position: "1"),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("The Way of Kings", result.Missing[0].Title);
        Assert.AreEqual(3, result.Missing[0].SeriesId);
        Assert.AreEqual("The Stormlight Archive", result.Missing[0].SeriesName);
        Assert.AreEqual("1", result.Missing[0].Position);
        Assert.AreEqual("55", result.Missing[0].SourceSeriesId);
    }

    // A series entry with a known local series is owned when the author owns a book of that same
    // series with a matching position/title - and a same-titled book in a DIFFERENT series must
    // not satisfy it.
    [TestMethod]
    public async Task GetReconciliationAsync_SeriesLinkedEntry_MatchesOwnedBookOfTheSameLocalSeries()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Words of Radiance", year: 2014,
                seriesId: 3, seriesName: "The Stormlight Archive", sourceSeriesId: "55", position: "2"),
        });
        SetupOwned(7, new List<SeriesOwnedKey>
        {
            new(101, "2", "Words of Radiance", "The Stormlight Archive"),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(1, result.OwnedCount);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_SeriesLinkedEntry_DoesNotMatchOwnedBookOfAnotherSeries()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Words of Radiance", year: 2014,
                seriesId: 3, seriesName: "The Stormlight Archive", sourceSeriesId: "55", position: "2"),
        });
        // Same title, same position - but in a different local series.
        SetupOwned(7, new List<SeriesOwnedKey>
        {
            new(101, "2", "Words of Radiance", "Another Series"),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.Missing.Count, "a book of a different series is not the same book");
    }

    [TestMethod]
    public async Task GetReconciliationAsync_RosterOverflow_Throws()
    {
        _expectedBookRepository
            .Setup(r => r.GetByAuthorBoundedAsync(7, It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBook>(), true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _provider.GetReconciliationAsync(7));
    }

    [TestMethod]
    public async Task GetReconciliationAsync_OwnedKeysOverflow_Throws()
    {
        SetupRoster(7, new List<ExpectedBook> { MakeBook(1, "Elantris") });
        _audiobookRepository
            .Setup(r => r.GetOwnedKeysByAuthorAsync(7, It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _provider.GetReconciliationAsync(7));
    }

    [TestMethod]
    public async Task GetReconciliationAsync_NoRoster_ReturnsEmptyReconciliation()
    {
        SetupRoster(999, new List<ExpectedBook>());

        var result = await _provider.GetReconciliationAsync(999);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(0, result.Ignored.Count);
        Assert.AreEqual(0, result.ExpectedBookCount);
        Assert.AreEqual(0, result.MissingSeries.Count);
    }

    // --- Missing series ------------------------------------------------------

    [TestMethod]
    public async Task GetReconciliationAsync_UnmatchedSourceSeriesWithZeroOwnedBooks_IsListedAsMissingSeries()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            MakeBook(1, "Forward", year: 2008, sourceSeriesId: "55", sourceSeriesName: "Skyward", position: "1"),
            MakeBook(2, "Starsight", year: 2019, sourceSeriesId: "55", sourceSeriesName: "Skyward", position: "2"),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.MissingSeries.Count);
        var missingSeries = result.MissingSeries.Single();
        Assert.AreEqual("Hardcover", missingSeries.SourceName);
        Assert.AreEqual("55", missingSeries.SourceSeriesId);
        Assert.AreEqual("Skyward", missingSeries.SourceSeriesName);
        Assert.IsNull(missingSeries.SeriesId, "an unmatched source series has no local series yet");
        Assert.AreEqual(2, missingSeries.ExpectedCount);
        Assert.AreEqual(2, missingSeries.MissingCount);
        Assert.AreEqual(0, missingSeries.UpcomingCount);
        Assert.AreEqual(0, missingSeries.OwnedCount);
    }

    // A series with some owned books is NOT a missing series - but its unmatched books still
    // surface in the author's Missing/Upcoming lists.
    [TestMethod]
    public async Task GetReconciliationAsync_PartiallyOwnedSeries_IsNotMissingSeriesButMissingBooksRemain()
    {
        SetupRoster(7, new List<ExpectedBook>
        {
            // book 1 is owned locally...
            MakeBook(1, "The Way of Kings", year: 2010,
                seriesId: 3, seriesName: "The Stormlight Archive", sourceSeriesId: "55", position: "1"),
            // ...book 3 is not.
            MakeBook(2, "Oathbringer", year: 2017,
                seriesId: 3, seriesName: "The Stormlight Archive", sourceSeriesId: "55", position: "3"),
        });
        SetupOwned(7, new List<SeriesOwnedKey>
        {
            new(101, "1", "The Way of Kings", "The Stormlight Archive"),
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.MissingSeries.Count, "a series with some owned books is not missing");
        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("Oathbringer", result.Missing[0].Title, "the unowned book stays visible in the author's missing list");
    }

    [TestMethod]
    public async Task GetReconciliationAsync_MoreSourceSeriesGroupsThanTheCap_Throws()
    {
        var oversized = Enumerable
            .Range(1, AuthorReconciliationProvider.MaxAuthorSeriesGroups + 1)
            .Select(i => MakeBook((long)i, $"Book {i}", year: 2005, sourceSeriesId: $"ss-{i}"))
            .ToList();
        SetupRoster(7, oversized);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _provider.GetReconciliationAsync(7));
    }

    // Review finding: the global upcoming view only needs the Missing/Upcoming classification, so
    // it can omit the bounded missing-series computation - a pathological group set that refuses
    // the author-detail computation must not take the whole upcoming page down. The cap/refusal
    // still applies when the detail asks for the groups (includeMissingSeries defaults to true).
    [TestMethod]
    public async Task GetReconciliationAsync_IncludeMissingSeriesFalse_OmitsTheGroupComputationEntirely()
    {
        var oversized = Enumerable
            .Range(1, AuthorReconciliationProvider.MaxAuthorSeriesGroups + 1)
            .Select(i => MakeBook((long)i, $"Book {i}", year: 2005, sourceSeriesId: $"ss-{i}"))
            .ToList();
        SetupRoster(7, oversized);

        var result = await _provider.GetReconciliationAsync(7, includeMissingSeries: false);

        Assert.AreEqual(0, result.MissingSeries.Count);
        Assert.AreEqual(oversized.Count, result.Missing.Count,
            "the missing/upcoming classification itself still runs");
    }

    // --- Bulk filter ---------------------------------------------------------

    private static ExpectedBookAuthorBookRef MakeRef(
        long personId, long bookId, string title, int? year = null, DateOnly? releaseDate = null,
        string? seriesName = null, string? sourceSeriesId = null, string? seriesPart = null) =>
        new(personId, bookId, title, year, releaseDate, seriesName, sourceSeriesId, seriesPart);

    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_ClassifiesEachAuthorSeparately()
    {
        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBookAuthorBookRef>
            {
                MakeRef(1, 1, "Elantris", 2005), // author 1: missing (past year, not owned)
                MakeRef(2, 2, "Warbreaker", DateTime.UtcNow.Year + 1), // author 2: upcoming
                MakeRef(3, 3, "Mistborn", 2006), // author 3: owned, neither
            }, false));
        SetupOwnedBulk(new List<AuthorOwnedKey>
        {
            // author 1 owns nothing: Elantris stays missing. author 2 owns nothing: Warbreaker
            // stays upcoming. author 3 owns Mistborn: neither classification fires.
            new(2, 102, null, "Some Owned Novel", null),
            new(3, 103, null, "Mistborn", null),
        });

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsFalse(result.Refused);
        CollectionAssert.AreEquivalent(new long[] { 1 }, result.HasMissingBooks.ToList());
        CollectionAssert.AreEquivalent(new long[] { 2 }, result.HasUpcomingBooks.ToList());

        // Removing the N+1: every rostered author's owned keys come back from ONE batched read,
        // not one query per author inside the loop.
        _audiobookRepository.Verify(
            r => r.GetOwnedKeysByAuthorsAsync(
                new List<long> { 1, 2, 3 },
                (int)(3L * (AuthorReconciliationProvider.MaxReconciliationOwnedKeys + 1))),
            Times.Once);
    }

    // The bulk classifier matches series-linked refs with the same series-scoped semantics as the
    // detail view: a ref linked to a local series is only owned by a book of that series.
    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_SeriesScopedMatching()
    {
        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBookAuthorBookRef>
            {
                // Same title/part, but in DIFFERENT series - the ref is from Stormlight, the
                // owned book from another series, so the ref is still missing.
                MakeRef(1, 1, "Words of Radiance", 2014, seriesName: "The Stormlight Archive", sourceSeriesId: "55", seriesPart: "2"),
                // This one IS owned in its own series.
                MakeRef(1, 2, "The Hero of Ages", 2008, seriesName: "Mistborn", sourceSeriesId: "56", seriesPart: "3"),
            }, false));
        SetupOwnedBulk(new List<AuthorOwnedKey>
        {
            new(1, 101, "3", "The Hero of Ages", "Mistborn"),
        });

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsFalse(result.Refused);
        CollectionAssert.AreEquivalent(new long[] { 1 }, result.HasMissingBooks.ToList());
        CollectionAssert.DoesNotContain(result.HasUpcomingBooks.ToList(), 1L);
    }

    // Regression: GetReconciliationAsync refuses (throws) an author whose roster exceeds
    // MaxReconciliationRosterEntries, so the detail view never reconciles a pathological roster
    // whole. The bulk classifier used to have no equivalent cap at all - one such author would
    // have every one of their entries matched/classified in full. A list-filter endpoint that
    // classifies every author with a roster in one pass must not throw over a single pathological
    // author (that would take the whole authors list down), so the capped author is left out of
    // both result sets instead, while every other author is still classified normally.
    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_AuthorPastRosterCap_IsExcludedFromBothSets()
    {
        var oversizedRoster = Enumerable
            .Range(1, AuthorReconciliationProvider.MaxReconciliationRosterEntries + 1)
            .Select(i => MakeRef(1, (long)i, $"Book {i}", 2005))
            .Append(MakeRef(2, 5001, "Warbreaker", DateTime.UtcNow.Year + 1))
            .ToList();

        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((oversizedRoster, false));
        SetupOwnedBulk(new List<AuthorOwnedKey>());

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsFalse(result.Refused);
        CollectionAssert.DoesNotContain(result.HasMissingBooks.ToList(), 1L);
        CollectionAssert.DoesNotContain(result.HasUpcomingBooks.ToList(), 1L);
        CollectionAssert.AreEquivalent(new long[] { 2 }, result.HasUpcomingBooks.ToList());
        // The oversized author is skipped by the roster cap before any owned keys are read, so
        // its person id must not spend a row of the batched read's budget.
        _audiobookRepository.Verify(
            r => r.GetOwnedKeysByAuthorsAsync(new List<long> { 2 }, It.IsAny<int>()), Times.Once);
    }

    // The per-author owned-key cap is enforced in memory on the batched read's grouped result: an
    // author past MaxReconciliationOwnedKeys is skipped exactly like the detail view refuses it,
    // while every other rostered author is still classified from the same single query.
    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_AuthorPastOwnedKeysCap_IsExcludedFromBothSets()
    {
        var oversizedOwned = Enumerable
            .Range(1, AuthorReconciliationProvider.MaxReconciliationOwnedKeys + 1)
            .Select(i => new AuthorOwnedKey(1, (long)i, null, $"Book {i}", null))
            .ToList();
        // Author 2 owns nothing (its only key is an unrelated title), so Warbreaker stays upcoming.
        oversizedOwned.Add(new AuthorOwnedKey(2, 5001, null, "Unrelated Owned Title", null));

        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBookAuthorBookRef>
            {
                MakeRef(1, 1, "Elantris", 2005),
                MakeRef(2, 2, "Warbreaker", DateTime.UtcNow.Year + 1),
            }, false));
        SetupOwnedBulk(oversizedOwned);

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsFalse(result.Refused);
        CollectionAssert.DoesNotContain(result.HasMissingBooks.ToList(), 1L);
        CollectionAssert.DoesNotContain(result.HasUpcomingBooks.ToList(), 1L);
        CollectionAssert.AreEquivalent(new long[] { 2 }, result.HasUpcomingBooks.ToList(),
            "an author whose own key set is under the cap is still classified from the same batched read");
    }

    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_NoActiveExpectedBooks_ReturnsEmptySets()
    {
        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBookAuthorBookRef>(), false));

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsFalse(result.Refused);
        Assert.AreEqual(0, result.HasMissingBooks.Count);
        Assert.AreEqual(0, result.HasUpcomingBooks.Count);
        _audiobookRepository.Verify(
            r => r.GetOwnedKeysByAuthorsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<int>()), Times.Never);
    }

    // Regression guard for the bounded-read contract: the whole-library refs set is read
    // capped+1 with an overflow flag, and an overflow must REFUSE the filter - never silently
    // truncate into a wrong result.
    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_RefsOverflow_RefusesTheFilter()
    {
        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBookAuthorBookRef>(), true));

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsTrue(result.Refused,
            "a library past the bulk classification's bounded read must refuse the filter, not silently truncate it");
        Assert.AreEqual(0, result.HasMissingBooks.Count);
        Assert.AreEqual(0, result.HasUpcomingBooks.Count);
        _audiobookRepository.Verify(
            r => r.GetOwnedKeysByAuthorsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<int>()), Times.Never);
    }

    // The batched owned-key read carries its own bounded-read contract: the flat total bound can
    // cut an author's keys mid-list, so a prefix past it cannot be trusted for ANY author - the
    // filter must refuse exactly like the refs overflow does, rather than classify from a short
    // list that can wrongly flag owned books as missing.
    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_OwnedKeysTotalOverflow_RefusesTheFilter()
    {
        _expectedBookRepository
            .Setup(r => r.GetActiveAuthorBookRefsAsync(It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBookAuthorBookRef>
            {
                MakeRef(1, 1, "Elantris", 2005),
            }, false));
        _audiobookRepository
            .Setup(r => r.GetOwnedKeysByAuthorsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<int>()))
            .ReturnsAsync((new List<AuthorOwnedKey>(), true));

        var result = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.IsTrue(result.Refused,
            "an owned-key total past the batched read's bound must refuse the filter, not classify from a truncated prefix");
        Assert.AreEqual(0, result.HasMissingBooks.Count);
        Assert.AreEqual(0, result.HasUpcomingBooks.Count);
    }
}