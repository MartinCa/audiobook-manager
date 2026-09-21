using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using SeriesExpectedBookInfo = AudiobookManager.Domain.SeriesExpectedBookInfo;
using SeriesPartMismatch = AudiobookManager.Domain.SeriesPartMismatch;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class UpcomingReleaseServiceTests
{
    private Mock<IPersonRepository> _personRepository = null!;
    private Mock<ISeriesRepository> _seriesRepository = null!;
    private Mock<IAuthorFollowRepository> _authorFollowRepository = null!;
    private Mock<ISeriesFollowRepository> _seriesFollowRepository = null!;
    private Mock<IUpcomingReleaseRepository> _upcomingReleaseRepository = null!;
    private Mock<IExpectedBookRepository> _expectedBookRepository = null!;
    private Mock<ISeriesReconciliationProvider> _seriesReconciliationProvider = null!;
    private Mock<IAuthorReconciliationProvider> _authorReconciliationProvider = null!;
    private Mock<ISeriesReconciliationCache> _seriesReconciliationCache = null!;
    private Mock<IScraper> _scraper = null!;
    private UpcomingReleaseService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _personRepository = new Mock<IPersonRepository>();
        _seriesRepository = new Mock<ISeriesRepository>();
        _authorFollowRepository = new Mock<IAuthorFollowRepository>();
        _seriesFollowRepository = new Mock<ISeriesFollowRepository>();
        _upcomingReleaseRepository = new Mock<IUpcomingReleaseRepository>();
        _expectedBookRepository = new Mock<IExpectedBookRepository>();
        // Default: the author refresh's cache-invalidation pre-read sees no current roster rows.
        _expectedBookRepository
            .Setup(r => r.GetByAuthorBoundedAsync(It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBook>(), false));
        _seriesReconciliationProvider = new Mock<ISeriesReconciliationProvider>();
        _authorReconciliationProvider = new Mock<IAuthorReconciliationProvider>();
        _seriesReconciliationCache = new Mock<ISeriesReconciliationCache>();

        _scraper = new Mock<IScraper>();
        _scraper.Setup(s => s.SourceName).Returns("Hardcover");
        _scraper.Setup(s => s.SupportsAuthorLookup).Returns(true);
        _scraper.Setup(s => s.RequiresApiKey).Returns(false);

        _service = new UpcomingReleaseService(
            _personRepository.Object,
            _seriesRepository.Object,
            _authorFollowRepository.Object,
            _seriesFollowRepository.Object,
            _upcomingReleaseRepository.Object,
            _expectedBookRepository.Object,
            _seriesReconciliationProvider.Object,
            _authorReconciliationProvider.Object,
            _seriesReconciliationCache.Object,
            new[] { _scraper.Object },
            Mock.Of<ILogger<UpcomingReleaseService>>());
    }

    [TestMethod]
    public async Task FollowAuthorAsync_UnknownPerson_ThrowsKeyNotFound()
    {
        _personRepository.Setup(r => r.GetByIdAsync(42)).ReturnsAsync((Person?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _service.FollowAuthorAsync(42));

        _authorFollowRepository.Verify(r => r.FollowAsync(It.IsAny<long>()), Times.Never);
    }

    [TestMethod]
    public async Task FollowAuthorAsync_KnownPerson_CallsRepository()
    {
        _personRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson"));

        await _service.FollowAuthorAsync(7);

        _authorFollowRepository.Verify(r => r.FollowAsync(7), Times.Once);
    }

    [TestMethod]
    public async Task MatchAuthorAsync_UnknownPerson_ThrowsKeyNotFound()
    {
        _personRepository.Setup(r => r.GetByIdAsync(42)).ReturnsAsync((Person?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.MatchAuthorAsync(42, "123", "Hardcover", null));

        _personRepository.Verify(
            r => r.SetAuthorMatchAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [TestMethod]
    public async Task MatchAuthorAsync_KnownPerson_PersistsTheMatchWithoutRefreshing()
    {
        _personRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson"));

        await _service.MatchAuthorAsync(7, "123", "Hardcover", "https://hardcover.app/authors/123");

        _personRepository.Verify(
            r => r.SetAuthorMatchAsync(7, "Hardcover", "123", "https://hardcover.app/authors/123"), Times.Once);
        // The match-triggered refresh is the CONTROLLER's job (it owns the author-refresh lock, see
        // BrowseController.MatchAuthor), so the service's MatchAuthorAsync must not scrape here.
        _scraper.Verify(s => s.GetAuthorBooks(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_UpsertsOneRowPerAuthorRelease()
    {
        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "123" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        var release = new UpcomingReleaseResult("999", "The Stormlight Archive 6", new DateOnly(2030, 1, 1));
        _scraper.Setup(s => s.GetAuthorUpcomingReleases("123"))
            .ReturnsAsync((IList<UpcomingReleaseResult>)new List<UpcomingReleaseResult> { release });

        await _service.RefreshUpcomingReleasesAsync();

        _upcomingReleaseRepository.Verify(r => r.UpsertAsync(It.Is<UpcomingRelease>(u =>
            u.SourceBookId == "999" && u.PersonId == 7 && u.SeriesId == null && u.SourceName == "Hardcover")), Times.Once);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_AttachesMatchingLocalSeriesFromAuthorRelease()
    {
        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "123" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        var release = new UpcomingReleaseResult("999", "The Stormlight Archive 6", new DateOnly(2030, 1, 1))
        {
            SeriesName = "The Stormlight Archive",
            SeriesSourceId = "55",
            SeriesPosition = "6",
        };
        _scraper.Setup(s => s.GetAuthorUpcomingReleases("123"))
            .ReturnsAsync((IList<UpcomingReleaseResult>)new List<UpcomingReleaseResult> { release });

        _seriesRepository.Setup(r => r.GetByMatchedSourceIdAsync("Hardcover", "55"))
            .ReturnsAsync(new Series { Id = 3, Name = "The Stormlight Archive (renamed locally)", MatchedSourceId = "55" });

        await _service.RefreshUpcomingReleasesAsync();

        _upcomingReleaseRepository.Verify(r => r.UpsertAsync(It.Is<UpcomingRelease>(u =>
            u.PersonId == 7 && u.SeriesId == 3 && u.SeriesPosition == "6")), Times.Once);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_DoesNotAttachASeriesWithNoLocalMatch()
    {
        // No local series is matched to this exact (source name, source id) pair - a
        // same-named-but-differently-matched (or entirely unmatched) local series must never be
        // linked by name alone.
        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "123" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        var release = new UpcomingReleaseResult("999", "Book 6", new DateOnly(2030, 1, 1))
        {
            SeriesName = "Some Series",
            SeriesSourceId = "55",
        };
        _scraper.Setup(s => s.GetAuthorUpcomingReleases("123"))
            .ReturnsAsync((IList<UpcomingReleaseResult>)new List<UpcomingReleaseResult> { release });

        _seriesRepository.Setup(r => r.GetByMatchedSourceIdAsync("Hardcover", "55")).ReturnsAsync((Series?)null);

        await _service.RefreshUpcomingReleasesAsync();

        _upcomingReleaseRepository.Verify(r => r.UpsertAsync(It.Is<UpcomingRelease>(u => u.SeriesId == null)), Times.Once);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_StopsAfterDailyLimitExceeded()
    {
        var author1 = new Person(1, "Author One") { MatchedSourceId = "1" };
        var author2 = new Person(2, "Author Two") { MatchedSourceId = "2" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync())
            .ReturnsAsync(new List<Person> { author1, author2 });
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        _scraper.Setup(s => s.GetAuthorUpcomingReleases("1")).ThrowsAsync(new HardcoverDailyLimitExceededException(5000));

        await _service.RefreshUpcomingReleasesAsync();

        _scraper.Verify(s => s.GetAuthorUpcomingReleases("2"), Times.Never);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_OneAuthorFailing_DoesNotStopTheRest()
    {
        var author1 = new Person(1, "Author One") { MatchedSourceId = "1" };
        var author2 = new Person(2, "Author Two") { MatchedSourceId = "2" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync())
            .ReturnsAsync(new List<Person> { author1, author2 });
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        _scraper.Setup(s => s.GetAuthorUpcomingReleases("1")).ThrowsAsync(new Exception("boom"));
        _scraper.Setup(s => s.GetAuthorUpcomingReleases("2"))
            .ReturnsAsync((IList<UpcomingReleaseResult>)new List<UpcomingReleaseResult>());

        await _service.RefreshUpcomingReleasesAsync();

        _scraper.Verify(s => s.GetAuthorUpcomingReleases("2"), Times.Once);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_SeriesRelease_HasNoPersonId()
    {
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person>());
        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });

        var release = new UpcomingReleaseResult("444", "Book 15", new DateOnly(2031, 1, 1));
        _scraper.Setup(s => s.GetSeriesUpcomingReleases("77"))
            .ReturnsAsync((IList<UpcomingReleaseResult>)new List<UpcomingReleaseResult> { release });

        await _service.RefreshUpcomingReleasesAsync();

        _upcomingReleaseRepository.Verify(r => r.UpsertAsync(It.Is<UpcomingRelease>(u =>
            u.SeriesId == 9 && u.PersonId == null && u.SourceBookId == "444")), Times.Once);
    }

    [TestMethod]
    public async Task RemoveUpcomingReleaseAsync_DelegatesToRepository()
    {
        _upcomingReleaseRepository.Setup(r => r.DeleteAsync(5)).ReturnsAsync(true);

        var result = await _service.RemoveUpcomingReleaseAsync(5);

        Assert.IsTrue(result);
    }

    // --- Author roster refresh (unified write path) --------------------------

    [TestMethod]
    public async Task RefreshAuthorRosterAsync_UnknownAuthor_ThrowsKeyNotFound()
    {
        _personRepository.Setup(r => r.GetByIdAsync(42)).ReturnsAsync((Person?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _service.RefreshAuthorRosterAsync(42));
    }

    [TestMethod]
    public async Task RefreshAuthorRosterAsync_UnmatchedAuthor_ThrowsKeyNotFound()
    {
        _personRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson"));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _service.RefreshAuthorRosterAsync(7));

        _scraper.Verify(s => s.GetAuthorBooks(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshAuthorRosterAsync_NoAuthorCapableScraper_ThrowsArgumentException()
    {
        var scraperNoAuthorLookup = new Mock<IScraper>();
        scraperNoAuthorLookup.Setup(s => s.SourceName).Returns("Goodreads");
        scraperNoAuthorLookup.Setup(s => s.SupportsAuthorLookup).Returns(false);
        var service = new UpcomingReleaseService(
            _personRepository.Object, _seriesRepository.Object, _authorFollowRepository.Object,
            _seriesFollowRepository.Object, _upcomingReleaseRepository.Object,
            _expectedBookRepository.Object,
            _seriesReconciliationProvider.Object, _authorReconciliationProvider.Object,
            _seriesReconciliationCache.Object, new[] { scraperNoAuthorLookup.Object },
            Mock.Of<ILogger<UpcomingReleaseService>>());
        _personRepository.Setup(r => r.GetByIdAsync(7))
            .ReturnsAsync(new Person(7, "Brandon Sanderson") { MatchedSourceId = "123" });

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.RefreshAuthorRosterAsync(7));
    }

    private async Task<Person> SetupRefreshableAuthorAsync(long personId = 7, string matchedSourceId = "123")
    {
        var author = new Person(personId, "Brandon Sanderson") { MatchedSourceId = matchedSourceId };
        _personRepository.Setup(r => r.GetByIdAsync(personId)).ReturnsAsync(author);
        _expectedBookRepository.Setup(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()))
            .ReturnsAsync(new List<long> { 1, 2, 3 });
        _expectedBookRepository.Setup(r => r.GetByAuthorBoundedAsync(personId, It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBook>(), false));
        return author;
    }

    // Regression (the primary fix of this phase): the author refresh RETAINS a book that belongs
    // to a series instead of dropping it - the unified roster attributes every bibliography book
    // to the author here while the series' own refresh stores its series placement.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_HappyPath_UpsertsEveryBookIncludingSeriesBooks_PrunesAndStamps()
    {
        await SetupRefreshableAuthorAsync();

        var standalone = new AuthorBookResult("1", "Elantris") { Year = 2005 };
        var partOfSeries = new AuthorBookResult("2", "Mistborn Book 1")
        {
            Year = 2006,
            SeriesSourceId = "55",
            SeriesName = "Mistborn",
            SeriesPosition = "1",
        };
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult> { standalone, partOfSeries });

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.Is<IReadOnlyList<ExpectedBookUpsert>>(upserts =>
            upserts.Count == 2
            && upserts.Any(u => u.Title == "Elantris" && u.SourceBookId == "1"
                && u.SeriesId == null && u.SourceSeriesId == null)
            && upserts.Any(u => u.Title == "Mistborn Book 1" && u.SourceBookId == "2"
                && u.SeriesPosition == "1" && u.SourceSeriesId == "55" && u.SourceSeriesName == "Mistborn")
        )), Times.Once);

        // The server-side series book went in too - nothing was dropped.
        _expectedBookRepository.Verify(r => r.PruneAuthorLinksAsync(7, It.IsAny<IReadOnlyList<long>>()), Times.Once);
        _expectedBookRepository.Verify(r => r.DeleteOrphanExpectedBooksAsync(), Times.Once);
        _personRepository.Verify(r => r.SetLastRefreshedAtAsync(7, It.IsAny<DateTime>()), Times.Once);
    }

    // Review finding: an author-shaped upsert carries NO compilation data - the bibliography feed
    // reports none, and a series refresh that ever tags the shared row as an omnibus must not be
    // cleared by a later author refresh of the same book.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_UpsertCarriesNoCompilationData()
    {
        await SetupRefreshableAuthorAsync();
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>
            {
                new AuthorBookResult("1", "Elantris"),
            });

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.Is<IReadOnlyList<ExpectedBookUpsert>>(upserts =>
            upserts.Single().IsCompilation == null)), Times.Once);
    }

    // Review finding: the author refresh rewrites shared unified rows, so the cached
    // reconciliation of EVERY local series it touches - rows it is about to unlink or re-place
    // AND rows the fetched books land on - must be invalidated, or the series detail stays stale
    // for the cache's whole TTL.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_InvalidatesEveryTouchedLocalSeriesCache()
    {
        await SetupRefreshableAuthorAsync();
        _expectedBookRepository
            .Setup(r => r.GetByAuthorBoundedAsync(7, It.IsAny<int>()))
            .ReturnsAsync((new List<ExpectedBook>
            {
                // Pre-refresh: a book already linked to one local series (about to be re-placed).
                new()
                {
                    Id = 1,
                    SourceName = "Hardcover",
                    Title = "Words of Radiance",
                    SeriesId = 3,
                    Series = new Series { Id = 3, Name = "Old Stormlight" },
                    FirstSeenAt = DateTime.UtcNow,
                    LastRefreshedAt = DateTime.UtcNow,
                },
            }, false));
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>
            {
                new AuthorBookResult("1", "Words of Radiance")
                {
                    SeriesSourceId = "55",
                    SeriesName = "The Stormlight Archive",
                    SeriesPosition = "2",
                },
            });
        // The fetched book's source series resolves to a DIFFERENT local row than the one the
        // book was previously linked to.
        _seriesRepository.Setup(r => r.GetByMatchedSourceIdAsync("Hardcover", "55"))
            .ReturnsAsync(new Series { Id = 4, Name = "Locally Renamed Stormlight", MatchedSourceId = "55" });

        await _service.RefreshAuthorRosterAsync(7);

        _seriesReconciliationCache.Verify(c => c.Invalidate("Old Stormlight"), Times.Once,
            "a series the refresh is about to unlink/re-place rows from must be invalidated");
        _seriesReconciliationCache.Verify(c => c.Invalidate("Locally Renamed Stormlight"), Times.Once,
            "a series the refresh links rows onto must be invalidated");
    }

    [TestMethod]
    public async Task RefreshAuthorRosterAsync_NoSeriesTouched_InvalidatesNoSeriesCache()
    {
        await SetupRefreshableAuthorAsync();
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>
            {
                new AuthorBookResult("1", "Elantris"),
            });

        await _service.RefreshAuthorRosterAsync(7);

        _seriesReconciliationCache.Verify(c => c.Invalidate(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshAuthorRosterAsync_EachUpsertCarriesTheRefreshingAuthorsLink()
    {
        await SetupRefreshableAuthorAsync();
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>
            {
                new AuthorBookResult("1", "Elantris"),
            });

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.Is<IReadOnlyList<ExpectedBookUpsert>>(upserts =>
            upserts.Count == 1
            && upserts.Single().Authors.Count == 1
            && upserts.Single().Authors.Single().PersonId == 7
            && upserts.Single().Authors.Single().AuthorName == "Brandon Sanderson"
        )), Times.Once);
    }

    // A book whose source series has a locally-matched catalog row is linked to it by the exact
    // (source name, source series id) pair - never by name - and an unmatched source series keeps
    // its source-series fields for a later match to link in place.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_ResolvesMatchedLocalSeries_AndKeepsSourceFieldsForUnmatched()
    {
        await SetupRefreshableAuthorAsync();

        var matched = new AuthorBookResult("2", "Words of Radiance")
        {
            SeriesSourceId = "55",
            SeriesName = "The Stormlight Archive",
            SeriesPosition = "2",
        };
        var unmatched = new AuthorBookResult("3", "The Lost Metal")
        {
            SeriesSourceId = "77",
            SeriesName = "The Wax and Wayne Series",
            SeriesPosition = "4",
        };
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult> { matched, unmatched });

        _seriesRepository.Setup(r => r.GetByMatchedSourceIdAsync("Hardcover", "55"))
            .ReturnsAsync(new Series { Id = 3, Name = "Locally Renamed Stormlight", MatchedSourceId = "55" });
        _seriesRepository.Setup(r => r.GetByMatchedSourceIdAsync("Hardcover", "77"))
            .ReturnsAsync((Series?)null);

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.Is<IReadOnlyList<ExpectedBookUpsert>>(upserts =>
            upserts.Single(u => u.SourceBookId == "2").SeriesId == 3
            && upserts.Single(u => u.SourceBookId == "2").SourceSeriesId == "55"
            && upserts.Single(u => u.SourceBookId == "3").SeriesId == null
            && upserts.Single(u => u.SourceBookId == "3").SourceSeriesId == "77"
            && upserts.Single(u => u.SourceBookId == "3").SourceSeriesName == "The Wax and Wayne Series"
            && upserts.Single(u => u.SourceBookId == "3").SeriesPosition == "4"
        )), Times.Once);
    }

    // The prune keep-list is exactly the fetched set, so a book the source no longer lists (or an
    // author's old legacy adoption) loses THIS author's link and nothing else - other authors'
    // links and any series link keep a book alive via the orphan cleanup.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_PrunesOnlyThisAuthorsLinks_ToExactlyTheFetchedSet()
    {
        await SetupRefreshableAuthorAsync();
        _expectedBookRepository.Setup(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()))
            .ReturnsAsync(new List<long> { 41, 42 });
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>
            {
                new AuthorBookResult("1", "Elantris"),
                new AuthorBookResult("2", "Warbreaker"),
            });

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.PruneAuthorLinksAsync(7, new[] { 41L, 42L }), Times.Once);
    }

    // An empty bibliography must prune this author's links to none and then delete only the true
    // orphans (no author links, no series) - a series-linked book or a book still linked to
    // another author is untouched.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_EmptyFetch_PrunesThisAuthorsLinksAndLeavesForeignedBooksAlone()
    {
        await SetupRefreshableAuthorAsync();
        _expectedBookRepository.Setup(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()))
            .ReturnsAsync(new List<long>());
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>());

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.Is<IReadOnlyList<ExpectedBookUpsert>>(upserts => upserts.Count == 0)), Times.Once);
        _expectedBookRepository.Verify(r => r.PruneAuthorLinksAsync(7, It.Is<IReadOnlyList<long>>(keep => keep.Count == 0)), Times.Once);
        _expectedBookRepository.Verify(r => r.DeleteOrphanExpectedBooksAsync(), Times.Once);
        _personRepository.Verify(r => r.SetLastRefreshedAtAsync(7, It.IsAny<DateTime>()), Times.Once);
    }

    // Regression for the review finding (high, data loss): a scraper that cannot RESOLVE the
    // author (unparseable source id, or a null/undefined authors_by_pk - deleted/merged
    // upstream or a transient empty response) must surface as a thrown error, never as the
    // empty bibliography that prunes this author's whole unified roster - ignore history
    // included - and orphans the books. The exception propagating out of the refresh is what
    // leaves the roster untouched; nothing is upserted, pruned, orphan-deleted or stamped.
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_ScraperCannotResolveAuthor_LeavesTheRosterUntouchedAndThrows()
    {
        await SetupRefreshableAuthorAsync();
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ThrowsAsync(new AuthorNotFoundException("Hardcover returned no author for source id \"123\""));

        await Assert.ThrowsExactlyAsync<AuthorNotFoundException>(() => _service.RefreshAuthorRosterAsync(7));

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()), Times.Never,
            "a failed fetch must not upsert anything");
        _expectedBookRepository.Verify(r => r.PruneAuthorLinksAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<long>>()), Times.Never,
            "a failed fetch must never prune, even to an empty keep-list");
        _expectedBookRepository.Verify(r => r.DeleteOrphanExpectedBooksAsync(), Times.Never);
        _personRepository.Verify(r => r.SetLastRefreshedAtAsync(It.IsAny<long>(), It.IsAny<DateTime>()), Times.Never,
            "a failed fetch must not stamp the author as freshly refetched");
    }

    // Ignore decisions survive a re-refresh through the unified row's identity, not a separate
    // title-based carry-over table: the upsert carries the same source book id, so
    // ExpectedBookRepository.UpsertAsync refreshes the existing row in place and never resets
    // IsIgnored (that preservation is asserted at the repository level).
    [TestMethod]
    public async Task RefreshAuthorRosterAsync_WritesTheSameSourceIdentity_SoARefreshFindsTheSameRow()
    {
        await SetupRefreshableAuthorAsync();
        _scraper.Setup(s => s.GetAuthorBooks("123"))
            .ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>
            {
                new AuthorBookResult("sb-7", "Elantris"),
            });

        await _service.RefreshAuthorRosterAsync(7);

        _expectedBookRepository.Verify(r => r.UpsertManyAsync(It.Is<IReadOnlyList<ExpectedBookUpsert>>(upserts =>
            upserts.Count == 1
            && upserts.Single().SourceName == "Hardcover"
            && upserts.Single().SourceBookId == "sb-7"
        )), Times.Once);
        _personRepository.Verify(r => r.SetLastRefreshedAtAsync(7, It.IsAny<DateTime>()), Times.Once);
    }

    [TestMethod]
    public async Task RefreshAllAuthorRostersAsync_NoAuthorCapableScraper_ReturnsEarlyWithStopReason()
    {
        var scraperNoAuthorLookup = new Mock<IScraper>();
        scraperNoAuthorLookup.Setup(s => s.SourceName).Returns("Goodreads");
        scraperNoAuthorLookup.Setup(s => s.SupportsAuthorLookup).Returns(false);
        var service = new UpcomingReleaseService(
            _personRepository.Object, _seriesRepository.Object, _authorFollowRepository.Object,
            _seriesFollowRepository.Object, _upcomingReleaseRepository.Object,
            _expectedBookRepository.Object,
            _seriesReconciliationProvider.Object, _authorReconciliationProvider.Object,
            _seriesReconciliationCache.Object, new[] { scraperNoAuthorLookup.Object },
            Mock.Of<ILogger<UpcomingReleaseService>>());

        var (processed, succeeded, failed, stopReason) = await service.RefreshAllAuthorRostersAsync();

        Assert.AreEqual(0, processed);
        Assert.AreEqual(0, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNotNull(stopReason);
        _personRepository.Verify(r => r.GetMatchedAuthorsAsync(), Times.Never);
    }

    [TestMethod]
    public async Task RefreshAllAuthorRostersAsync_NoMatchedAuthors_ReturnsZerosWithNoStopReason()
    {
        _personRepository.Setup(r => r.GetMatchedAuthorsAsync()).ReturnsAsync(new List<Person>());

        var (processed, succeeded, failed, stopReason) = await _service.RefreshAllAuthorRostersAsync();

        // Distinguishes "budget exhausted"/"no scraper" from an unremarkable "nothing to do":
        // both would otherwise return the same all-zero tuple.
        Assert.AreEqual(0, processed);
        Assert.AreEqual(0, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNull(stopReason);
    }

    [TestMethod]
    public async Task RefreshAllAuthorRostersAsync_ProcessesEveryMatchedAuthor()
    {
        var author1 = new Person(1, "Author One") { MatchedSourceId = "1" };
        var author2 = new Person(2, "Author Two") { MatchedSourceId = "2" };
        _personRepository.Setup(r => r.GetMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author1, author2 });
        _expectedBookRepository.Setup(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()))
            .ReturnsAsync(new List<long>());
        _scraper.Setup(s => s.GetAuthorBooks("1")).ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>());
        _scraper.Setup(s => s.GetAuthorBooks("2")).ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>());

        var (processed, succeeded, failed, stopReason) = await _service.RefreshAllAuthorRostersAsync();

        Assert.AreEqual(2, processed);
        Assert.AreEqual(2, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNull(stopReason);
        _personRepository.Verify(r => r.SetLastRefreshedAtAsync(1, It.IsAny<DateTime>()), Times.Once);
        _personRepository.Verify(r => r.SetLastRefreshedAtAsync(2, It.IsAny<DateTime>()), Times.Once);
    }

    [TestMethod]
    public async Task RefreshAllAuthorRostersAsync_DailyLimitExceeded_StopsEarlyWithStopReason()
    {
        var author1 = new Person(1, "Author One") { MatchedSourceId = "1" };
        var author2 = new Person(2, "Author Two") { MatchedSourceId = "2" };
        _personRepository.Setup(r => r.GetMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author1, author2 });
        _expectedBookRepository.Setup(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()))
            .ReturnsAsync(new List<long>());
        _scraper.Setup(s => s.GetAuthorBooks("1")).ThrowsAsync(new HardcoverDailyLimitExceededException(5000));

        var (processed, succeeded, failed, stopReason) = await _service.RefreshAllAuthorRostersAsync();

        Assert.AreEqual(0, processed, "the author that hit the daily limit does not count as processed");
        Assert.AreEqual(0, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNotNull(stopReason);
        _scraper.Verify(s => s.GetAuthorBooks("2"), Times.Never);
    }

    [TestMethod]
    public async Task RefreshAllAuthorRostersAsync_OneAuthorFailing_DoesNotStopTheRestAndHasNoStopReason()
    {
        var author1 = new Person(1, "Author One") { MatchedSourceId = "1" };
        var author2 = new Person(2, "Author Two") { MatchedSourceId = "2" };
        _personRepository.Setup(r => r.GetMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author1, author2 });
        _expectedBookRepository.Setup(r => r.UpsertManyAsync(It.IsAny<IReadOnlyList<ExpectedBookUpsert>>()))
            .ReturnsAsync(new List<long>());
        _scraper.Setup(s => s.GetAuthorBooks("1")).ThrowsAsync(new Exception("boom"));
        _scraper.Setup(s => s.GetAuthorBooks("2")).ReturnsAsync((IList<AuthorBookResult>)new List<AuthorBookResult>());

        var (processed, succeeded, failed, stopReason) = await _service.RefreshAllAuthorRostersAsync();

        Assert.AreEqual(2, processed);
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(1, failed);
        Assert.IsNull(stopReason);
    }

    // --- Dismissal on the shared row -----------------------------------------

    [TestMethod]
    public async Task DismissAuthorRosterUpcomingAsync_SetsIgnoredOnTheSharedUnifiedRow()
    {
        _expectedBookRepository
            .Setup(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Standalone Novella", true, AuthorReconciliationProvider.MaxReconciliationRosterEntries))
            .ReturnsAsync((long?)null);

        await _service.DismissAuthorRosterUpcomingAsync(7, "Standalone Novella");

        _expectedBookRepository.Verify(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Standalone Novella", true, AuthorReconciliationProvider.MaxReconciliationRosterEntries), Times.Once);
    }

    [TestMethod]
    public async Task RestoreAuthorRosterUpcomingAsync_ClearsIgnoredOnTheSharedUnifiedRow()
    {
        _expectedBookRepository
            .Setup(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Standalone Novella", false, AuthorReconciliationProvider.MaxReconciliationRosterEntries))
            .ReturnsAsync((long?)null);

        await _service.RestoreAuthorRosterUpcomingAsync(7, "Standalone Novella");

        _expectedBookRepository.Verify(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Standalone Novella", false, AuthorReconciliationProvider.MaxReconciliationRosterEntries), Times.Once);
    }

    // The dismissal targets the SHARED row a series refresh may also link to - the series'
    // cached reconciliation renders from that row, so a dismissal through the author scope must
    // invalidate the series cache, or the series detail stays stale for the cache's whole TTL.
    [TestMethod]
    public async Task DismissAuthorRosterUpcomingAsync_SeriesLinkedRow_InvalidatesThatSeriesCache()
    {
        _expectedBookRepository
            .Setup(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Words of Radiance", true, AuthorReconciliationProvider.MaxReconciliationRosterEntries))
            .ReturnsAsync((long?)9);
        _seriesRepository.Setup(r => r.GetNameByIdAsync(9)).ReturnsAsync("The Stormlight Archive");

        await _service.DismissAuthorRosterUpcomingAsync(7, "Words of Radiance");

        _seriesReconciliationCache.Verify(c => c.Invalidate("The Stormlight Archive"), Times.Once);
    }

    // Restoring the flag has the same cache obligation as dismissing it.
    [TestMethod]
    public async Task RestoreAuthorRosterUpcomingAsync_SeriesLinkedRow_InvalidatesThatSeriesCache()
    {
        _expectedBookRepository
            .Setup(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Words of Radiance", false, AuthorReconciliationProvider.MaxReconciliationRosterEntries))
            .ReturnsAsync((long?)9);
        _seriesRepository.Setup(r => r.GetNameByIdAsync(9)).ReturnsAsync("The Stormlight Archive");

        await _service.RestoreAuthorRosterUpcomingAsync(7, "Words of Radiance");

        _seriesReconciliationCache.Verify(c => c.Invalidate("The Stormlight Archive"), Times.Once);
    }

    // A standalone row (no series link) needs no series-cache invalidation.
    [TestMethod]
    public async Task DismissAuthorRosterUpcomingAsync_StandaloneRow_DoesNotInvalidateAnySeriesCache()
    {
        _expectedBookRepository
            .Setup(r => r.SetExpectedBookIgnoredByPersonAsync(7, "Elantris", true, AuthorReconciliationProvider.MaxReconciliationRosterEntries))
            .ReturnsAsync((long?)null);

        await _service.DismissAuthorRosterUpcomingAsync(7, "Elantris");

        _seriesRepository.Verify(r => r.GetNameByIdAsync(It.IsAny<long>()), Times.Never);
        _seriesReconciliationCache.Verify(c => c.Invalidate(It.IsAny<string>()), Times.Never);
    }

    // The id-addressed dismissal path (stable ExpectedBook row id, no title ambiguity): reads the
    // row, sets the flag by id, and invalidates any linked series' cache.
    [TestMethod]
    public async Task DismissAuthorRosterUpcomingByIdAsync_SetsIgnoredById_AndInvalidatesTheLinkedSeriesCache()
    {
        _expectedBookRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ExpectedBook
        {
            Id = 5,
            SourceName = "Hardcover",
            Title = "Words of Radiance",
            SeriesId = 9,
            FirstSeenAt = DateTime.UtcNow,
            LastRefreshedAt = DateTime.UtcNow,
        });
        _seriesRepository.Setup(r => r.GetNameByIdAsync(9)).ReturnsAsync("The Stormlight Archive");

        await _service.DismissAuthorRosterUpcomingByIdAsync(5);

        _expectedBookRepository.Verify(r => r.SetIgnoredByIdAsync(5, true), Times.Once);
        _seriesReconciliationCache.Verify(c => c.Invalidate("The Stormlight Archive"), Times.Once);
    }

    [TestMethod]
    public async Task RestoreAuthorRosterUpcomingByIdAsync_SetsIgnoredFalseById()
    {
        _expectedBookRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ExpectedBook
        {
            Id = 5,
            SourceName = "Hardcover",
            Title = "Elantris",
            FirstSeenAt = DateTime.UtcNow,
            LastRefreshedAt = DateTime.UtcNow,
        });

        await _service.RestoreAuthorRosterUpcomingByIdAsync(5);

        _expectedBookRepository.Verify(r => r.SetIgnoredByIdAsync(5, false), Times.Once);
        _seriesReconciliationCache.Verify(c => c.Invalidate(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task DismissAuthorRosterUpcomingByIdAsync_UnknownId_ThrowsKeyNotFound()
    {
        _expectedBookRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync((ExpectedBook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _service.DismissAuthorRosterUpcomingByIdAsync(5));

        _expectedBookRepository.Verify(r => r.SetIgnoredByIdAsync(It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
    }

    // The source-identity dismissal route (the dedup key the merged upcoming item exposes):
    // resolves the row by (source name, source book id), sets the flag by its stable id, and
    // invalidates any linked series' cache - the same shape as the id route.
    [TestMethod]
    public async Task DismissRosterUpcomingBySourceAsync_SetsIgnoredById_AndInvalidatesTheLinkedSeriesCache()
    {
        _expectedBookRepository.Setup(r => r.GetBySourceAsync("Hardcover", "555")).ReturnsAsync(new ExpectedBook
        {
            Id = 5,
            SourceName = "Hardcover",
            SourceBookId = "555",
            Title = "Words of Radiance",
            SeriesId = 9,
            FirstSeenAt = DateTime.UtcNow,
            LastRefreshedAt = DateTime.UtcNow,
        });
        _seriesRepository.Setup(r => r.GetNameByIdAsync(9)).ReturnsAsync("The Stormlight Archive");

        await _service.DismissRosterUpcomingBySourceAsync("Hardcover", "555");

        _expectedBookRepository.Verify(r => r.SetIgnoredByIdAsync(5, true), Times.Once);
        _seriesReconciliationCache.Verify(c => c.Invalidate("The Stormlight Archive"), Times.Once);
    }

    [TestMethod]
    public async Task DismissRosterUpcomingBySourceAsync_UnknownIdentity_ThrowsKeyNotFound()
    {
        _expectedBookRepository.Setup(r => r.GetBySourceAsync("Hardcover", "nope")).ReturnsAsync((ExpectedBook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.DismissRosterUpcomingBySourceAsync("Hardcover", "nope"));

        _expectedBookRepository.Verify(r => r.SetIgnoredByIdAsync(It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task DismissSeriesRosterUpcomingAsync_SetsIgnoredAndInvalidatesCache()
    {
        await _service.DismissSeriesRosterUpcomingAsync("The Wheel of Time", "15", "Book 15");

        _seriesRepository.Verify(r => r.SetExpectedBookIgnoredAsync("The Wheel of Time", "15", "Book 15", true, SeriesReconciliationProvider.MaxReconciliationRosterEntries), Times.Once);
        _seriesReconciliationCache.Verify(c => c.Invalidate("The Wheel of Time"), Times.Once);
    }

    // --- Merged upcoming-releases view ---------------------------------------

    private static SeriesReconciliation SeriesReconciliationWith(
        List<SeriesExpectedBookInfo> upcoming, List<SeriesExpectedBookInfo>? ignored = null) =>
        new(
            Missing: new List<SeriesExpectedBookInfo>(),
            Ignored: ignored ?? new List<SeriesExpectedBookInfo>(),
            PartMismatches: new List<SeriesPartMismatch>(),
            ExpectedBookCount: 1,
            OwnedCount: 0,
            Authors: new List<string>(),
            Upcoming: upcoming,
            IgnoredMissing: new List<SeriesExpectedBookInfo>(),
            IgnoredUpcoming: new List<SeriesExpectedBookInfo>());

    private static AuthorReconciliation AuthorReconciliationWith(
        List<AuthorExpectedBookInfo> upcoming, List<AuthorExpectedBookInfo>? ignored = null) =>
        new(
            Missing: new List<AuthorExpectedBookInfo>(),
            Upcoming: upcoming,
            Ignored: ignored ?? new List<AuthorExpectedBookInfo>(),
            ExpectedBookCount: 1,
            OwnedCount: 0,
            MissingSeries: new List<AuthorMissingSeriesInfo>());

    [TestMethod]
    public async Task GetUpcomingReleasesAsync_UnscopedQuery_IncludesRosterUpcomingForFollowedMatchedSeriesAndAuthors()
    {
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease>());

        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(new List<SeriesExpectedBookInfo>
            {
                new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1), SourceName = "Hardcover", SourceBookId = "444" },
            }));

        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "42" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _authorReconciliationProvider.Setup(p => p.GetReconciliationAsync(7, false)).ReturnsAsync(
            AuthorReconciliationWith(new List<AuthorExpectedBookInfo>
            {
                new() { Id = 2, Title = "Standalone Novella", Year = 2031, ReleaseDate = new DateOnly(2031, 2, 1), SourceName = "Hardcover", SourceBookId = "999" },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(2, total);
        Assert.IsTrue(items.All(i => i.Source == UpcomingReleaseSource.Roster));
        Assert.IsTrue(items.Any(i => i.Title == "Book 15" && i.SeriesId == 9 && i.SeriesName == "The Wheel of Time"));
        Assert.IsTrue(items.Any(i => i.Title == "Standalone Novella" && i.AuthorId == 7 && i.AuthorName == "Brandon Sanderson"));
    }

    // A followed author's series books must appear in HER scope even when the series is not
    // followed - the unified roster carries them through the author scope.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_AuthorScopedQuery_IncludesHerSeriesBooks()
    {
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(7, null)).ReturnsAsync(new List<UpcomingRelease>());
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "42" };
        _personRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(author);
        _authorFollowRepository.Setup(r => r.IsFollowedAsync(7)).ReturnsAsync(true);
        _authorReconciliationProvider.Setup(p => p.GetReconciliationAsync(7, false)).ReturnsAsync(
            AuthorReconciliationWith(new List<AuthorExpectedBookInfo>
            {
                new()
                {
                    Id = 3,
                    Title = "Words of Radiance",
                    Position = "2",
                    Year = 2032,
                    ReleaseDate = new DateOnly(2032, 1, 1),
                    SourceName = "Hardcover",
                    SourceBookId = "555",
                    SeriesId = 11,
                    SeriesName = "The Stormlight Archive",
                    SourceSeriesId = "55",
                },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(7, null, 50, 0);

        Assert.AreEqual(1, total);
        var item = items.Single();
        Assert.AreEqual(UpcomingReleaseSource.Roster, item.Source);
        // The book keeps its series representation AND the author id used for dismissal.
        Assert.AreEqual("Words of Radiance", item.Title);
        Assert.AreEqual(11, item.SeriesId);
        Assert.AreEqual("The Stormlight Archive", item.SeriesName);
        Assert.AreEqual("2", item.SeriesPosition);
        Assert.AreEqual(7, item.AuthorId);
        Assert.AreEqual("Hardcover", item.SourceName);
        Assert.AreEqual("555", item.SourceBookId);
        Assert.AreEqual(3, item.ExpectedBookId, "a roster item always carries its unified row id for the dismiss-by-id route");
    }

    // Roster-derived upcoming items carry the stored cover image so the "what's coming up" view can
    // render it without a separate lookup - the author side stores it from the bibliography poll,
    // the series side from its roster poll.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_RosterItems_CarryTheStoredCoverImageUrl()
    {
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease>());

        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(new List<SeriesExpectedBookInfo>
            {
                new()
                {
                    Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1),
                    SourceName = "Hardcover", SourceBookId = "444", ImageUrl = "https://covers.hardcover.app/book-15.jpg",
                },
            }));

        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "42" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _authorReconciliationProvider.Setup(p => p.GetReconciliationAsync(7, false)).ReturnsAsync(
            AuthorReconciliationWith(new List<AuthorExpectedBookInfo>
            {
                new()
                {
                    Id = 2, Title = "Standalone Novella", Year = 2031, ReleaseDate = new DateOnly(2031, 2, 1),
                    SourceName = "Hardcover", SourceBookId = "999", ImageUrl = "https://covers.hardcover.app/novella.jpg",
                },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(2, total);
        Assert.AreEqual("https://covers.hardcover.app/book-15.jpg", items.Single(i => i.Title == "Book 15").ImageUrl);
        Assert.AreEqual("https://covers.hardcover.app/novella.jpg", items.Single(i => i.Title == "Standalone Novella").ImageUrl);
    }

    // The same source identity discovered through both the followed author and the followed
    // series it belongs to must produce ONE item that keeps both the series representation and
    // the author id.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_SameSourceIdentityInAuthorAndSeriesScope_EmitsOneMergedItem()
    {
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease>());

        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(new List<SeriesExpectedBookInfo>
            {
                new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1), SourceName = "Hardcover", SourceBookId = "444" },
            }));

        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "42" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _authorReconciliationProvider.Setup(p => p.GetReconciliationAsync(7, false)).ReturnsAsync(
            AuthorReconciliationWith(new List<AuthorExpectedBookInfo>
            {
                new()
                {
                    Id = 5,
                    Title = "Book 15",
                    Position = "15",
                    Year = 2031,
                    ReleaseDate = new DateOnly(2031, 1, 1),
                    SourceName = "Hardcover",
                    SourceBookId = "444",
                    SeriesId = 9,
                    SeriesName = "The Wheel of Time",
                    SourceSeriesId = "77",
                },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(1, total, "one source book, one item - not one per scope");
        var item = items.Single();
        Assert.AreEqual(UpcomingReleaseSource.Roster, item.Source);
        Assert.AreEqual(9, item.SeriesId, "the series-linked representation wins");
        Assert.AreEqual("The Wheel of Time", item.SeriesName);
        Assert.AreEqual("15", item.SeriesPosition);
        Assert.AreEqual(7, item.AuthorId, "the author id stays available for filtering/dismissal");
        Assert.AreEqual("Brandon Sanderson", item.AuthorName);
        Assert.AreEqual("Hardcover", item.SourceName);
        Assert.AreEqual("444", item.SourceBookId);
        Assert.AreEqual(1, item.ExpectedBookId,
            "the merged item carries the stable unified row id the dismiss-by-id route needs");
    }

    [TestMethod]
    public async Task GetUpcomingReleasesAsync_LegacyRowMatchingRosterEntryBySourceIdentity_IsDeduped()
    {
        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(new List<SeriesExpectedBookInfo>
            {
                new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1), SourceName = "Hardcover", SourceBookId = "444" },
            }));
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person>());

        // The legacy scrape table already knows about the exact same book, under the same series.
        var legacyDuplicate = new UpcomingRelease
        {
            Id = 100,
            Title = "Book 15",
            ReleaseDate = new DateOnly(2031, 1, 1),
            SeriesId = 9,
            Series = series,
            SourceName = "Hardcover",
            SourceBookId = "444",
        };
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease> { legacyDuplicate });

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual(UpcomingReleaseSource.Roster, items.Single().Source);
    }

    // Legacy rows that share NO roster source identity still surface, and remain dismissible by
    // their own row id - the legacy pipeline keeps working alongside the unified one.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_LegacyRowWithNoRosterMatch_StillSurfaces()
    {
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person>());
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease>
        {
            new() { Id = 100, Title = "Book 15", ReleaseDate = new DateOnly(2031, 1, 1), SourceName = "Hardcover", SourceBookId = "444" },
        });

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(1, total);
        var item = items.Single();
        Assert.AreEqual(UpcomingReleaseSource.Legacy, item.Source);
        Assert.AreEqual(100, item.Id);
        Assert.AreEqual("444", item.SourceBookId);
    }

    // A dismissed (ignored) roster entry must keep suppressing its legacy duplicate even though
    // it is itself invisible - global ignore hides the shared row everywhere at once.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_IgnoredRosterEntry_StillSuppressesItsLegacyDuplicate()
    {
        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(
                upcoming: new List<SeriesExpectedBookInfo>(),
                ignored: new List<SeriesExpectedBookInfo>
                {
                    new() { Id = 1, Title = "Book 15", Position = "15", SourceName = "Hardcover", SourceBookId = "444", IsIgnored = true },
                }));
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person>());

        var legacyDuplicate = new UpcomingRelease
        {
            Id = 100,
            Title = "Book 15",
            ReleaseDate = new DateOnly(2031, 1, 1),
            SeriesId = 9,
            Series = series,
            SourceName = "Hardcover",
            SourceBookId = "444",
        };
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease> { legacyDuplicate });

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(0, total, "the ignored roster entry is invisible AND keeps the legacy duplicate invisible");
    }

    [TestMethod]
    public async Task GetUpcomingReleasesAsync_SeriesScopedQuery_OnlyThatSeriesRemains()
    {
        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, 9)).ReturnsAsync(new List<UpcomingRelease>());
        _seriesRepository.Setup(r => r.GetByIdWithExpectedBooksAsync(9)).ReturnsAsync(series);
        _seriesFollowRepository.Setup(r => r.IsFollowedAsync(9)).ReturnsAsync(true);
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(new List<SeriesExpectedBookInfo>
            {
                new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1), SourceName = "Hardcover", SourceBookId = "444" },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, 9, 50, 0);

        Assert.AreEqual(1, total);
        Assert.AreEqual(9, items.Single().SeriesId);
        _authorFollowRepository.Verify(r => r.GetFollowedMatchedAuthorsAsync(), Times.Never);
    }

    // Regression: the series detail page's scoped call must show roster Upcoming entries for a
    // MATCHED series regardless of follow status - per UPCOMING_RELEASES_DESIGN.md ("The
    // per-series/per-author detail page shows missing/upcoming regardless of follow status").
    // IsFollowedAsync is deliberately left unconfigured (Moq default: false) and never verified as
    // called - the scoped branch must not even need to check it.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_SeriesScopedQuery_MatchedButUnfollowed_StillReturnsRosterUpcoming()
    {
        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, 9)).ReturnsAsync(new List<UpcomingRelease>());
        _seriesRepository.Setup(r => r.GetByIdWithExpectedBooksAsync(9)).ReturnsAsync(series);
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            SeriesReconciliationWith(new List<SeriesExpectedBookInfo>
            {
                new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1), SourceName = "Hardcover", SourceBookId = "444" },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, 9, 50, 0);

        Assert.AreEqual(1, total, "an unfollowed but matched series' scoped page must still show its roster Upcoming entries");
        Assert.AreEqual(9, items.Single().SeriesId);
        Assert.AreEqual("Book 15", items.Single().Title);
    }

    // Same regression for the author detail page's scoped call.
    [TestMethod]
    public async Task GetUpcomingReleasesAsync_AuthorScopedQuery_MatchedButUnfollowed_StillReturnsRosterUpcoming()
    {
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(7, null)).ReturnsAsync(new List<UpcomingRelease>());
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series>());

        var author = new Person(7, "Brandon Sanderson") { MatchedSourceId = "42" };
        _personRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(author);
        _authorReconciliationProvider.Setup(p => p.GetReconciliationAsync(7, false)).ReturnsAsync(
            AuthorReconciliationWith(new List<AuthorExpectedBookInfo>
            {
                new() { Id = 2, Title = "Standalone Novella", Year = 2031, ReleaseDate = new DateOnly(2031, 2, 1), SourceName = "Hardcover", SourceBookId = "999" },
            }));

        var (items, total) = await _service.GetUpcomingReleasesAsync(7, null, 50, 0);

        Assert.AreEqual(1, total, "an unfollowed but matched author's scoped page must still show their roster Upcoming entries");
        Assert.AreEqual("Standalone Novella", items.Single().Title);
        Assert.AreEqual(7, items.Single().AuthorId);
    }
}