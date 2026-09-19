using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
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
            r => r.SetHardcoverMatchAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [TestMethod]
    public async Task MatchAuthorAsync_KnownPerson_PersistsTheMatch()
    {
        _personRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson"));

        await _service.MatchAuthorAsync(7, "123", "Hardcover", "https://hardcover.app/authors/123");

        _personRepository.Verify(
            r => r.SetHardcoverMatchAsync(7, "123", "Hardcover", "https://hardcover.app/authors/123"), Times.Once);
    }

    [TestMethod]
    public async Task RefreshUpcomingReleasesAsync_UpsertsOneRowPerAuthorRelease()
    {
        var author = new Person(7, "Brandon Sanderson") { HardcoverAuthorId = "123" };
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
        var author = new Person(7, "Brandon Sanderson") { HardcoverAuthorId = "123" };
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
        var author = new Person(7, "Brandon Sanderson") { HardcoverAuthorId = "123" };
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
        var author1 = new Person(1, "Author One") { HardcoverAuthorId = "1" };
        var author2 = new Person(2, "Author Two") { HardcoverAuthorId = "2" };
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
        var author1 = new Person(1, "Author One") { HardcoverAuthorId = "1" };
        var author2 = new Person(2, "Author Two") { HardcoverAuthorId = "2" };
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

    [TestMethod]
    public async Task GetUpcomingReleasesAsync_UnscopedQuery_IncludesRosterUpcomingForFollowedMatchedSeriesAndAuthors()
    {
        _upcomingReleaseRepository.Setup(r => r.GetAllAsync(null, null)).ReturnsAsync(new List<UpcomingRelease>());

        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            new SeriesReconciliation(
                Missing: new List<SeriesExpectedBookInfo>(),
                Ignored: new List<SeriesExpectedBookInfo>(),
                PartMismatches: new List<SeriesPartMismatch>(),
                ExpectedBookCount: 1,
                OwnedCount: 0,
                Authors: new List<string>(),
                Upcoming: new List<SeriesExpectedBookInfo>
                {
                    new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1) },
                }));

        var author = new Person(7, "Brandon Sanderson") { HardcoverAuthorId = "42" };
        _authorFollowRepository.Setup(r => r.GetFollowedMatchedAuthorsAsync()).ReturnsAsync(new List<Person> { author });
        _authorReconciliationProvider.Setup(p => p.GetReconciliationAsync(7)).ReturnsAsync(
            new AuthorReconciliation(
                Missing: new List<AuthorExpectedBookInfo>(),
                Upcoming: new List<AuthorExpectedBookInfo>
                {
                    new() { Id = 2, Title = "Standalone Novella", Year = 2031, ReleaseDate = new DateOnly(2031, 2, 1) },
                },
                ExpectedBookCount: 1,
                OwnedCount: 0));

        var (items, total) = await _service.GetUpcomingReleasesAsync(null, null, 50, 0);

        Assert.AreEqual(2, total);
        Assert.IsTrue(items.All(i => i.Source == UpcomingReleaseSource.Roster));
        Assert.IsTrue(items.Any(i => i.Title == "Book 15" && i.SeriesId == 9 && i.SeriesName == "The Wheel of Time"));
        Assert.IsTrue(items.Any(i => i.Title == "Standalone Novella" && i.AuthorId == 7 && i.AuthorName == "Brandon Sanderson"));
    }

    [TestMethod]
    public async Task GetUpcomingReleasesAsync_LegacyRowMatchingRosterEntryBySameTitle_IsDeduped()
    {
        var series = new Series { Id = 9, Name = "The Wheel of Time", MatchedSourceId = "77", MatchedSourceName = "Hardcover" };
        _seriesFollowRepository.Setup(r => r.GetFollowedMatchedSeriesAsync()).ReturnsAsync(new List<Series> { series });
        _seriesReconciliationProvider.Setup(p => p.GetReconciliationAsync("The Wheel of Time")).ReturnsAsync(
            new SeriesReconciliation(
                Missing: new List<SeriesExpectedBookInfo>(),
                Ignored: new List<SeriesExpectedBookInfo>(),
                PartMismatches: new List<SeriesPartMismatch>(),
                ExpectedBookCount: 1,
                OwnedCount: 0,
                Authors: new List<string>(),
                Upcoming: new List<SeriesExpectedBookInfo>
                {
                    new() { Id = 1, Title = "Book 15", Position = "15", Year = 2031, ReleaseDate = new DateOnly(2031, 1, 1) },
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

    [TestMethod]
    public async Task DismissSeriesRosterUpcomingAsync_SetsIgnoredAndInvalidatesCache()
    {
        await _service.DismissSeriesRosterUpcomingAsync("The Wheel of Time", "15", "Book 15");

        _seriesRepository.Verify(r => r.SetExpectedBookIgnoredAsync("The Wheel of Time", "15", "Book 15", true), Times.Once);
        _seriesReconciliationCache.Verify(c => c.Invalidate("The Wheel of Time"), Times.Once);
    }

    [TestMethod]
    public async Task DismissAuthorRosterUpcomingAsync_SetsIgnored()
    {
        await _service.DismissAuthorRosterUpcomingAsync(7, "Standalone Novella");

        _personRepository.Verify(r => r.SetAuthorExpectedBookIgnoredAsync(7, "Standalone Novella", true), Times.Once);
    }
}
