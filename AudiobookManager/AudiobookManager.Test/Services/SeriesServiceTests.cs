using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DbPerson = AudiobookManager.Database.Models.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class SeriesServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<ISeriesRepository> _seriesRepository = null!;
    private Mock<ILogger<SeriesService>> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _seriesRepository = new Mock<ISeriesRepository>();
        _logger = new Mock<ILogger<SeriesService>>();
    }

    private SeriesService MakeService(params IScraper[] scrapers) =>
        new(_audiobookRepository.Object, _seriesRepository.Object, scrapers, _logger.Object);

    private static DbAudiobook MakeDbAudiobook(
        long id,
        string bookName,
        string? series = null,
        string? seriesPart = null,
        string? author = null)
    {
        var book = new DbAudiobook(id, bookName, null, series, seriesPart, 2024, null, null, null, null, null, null,
            null, null, $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);

        if (author is not null)
        {
            book.Authors.Add(new DbPerson(id, author));
        }

        return book;
    }

    private static SeriesExpectedBook MakeExpected(long id, string title, string? position, bool ignored = false) =>
        new() { Id = id, SeriesId = 1, Title = title, Position = position, IsIgnored = ignored };

    [TestMethod]
    public async Task GetSeriesDetailAsync_ReportsOnlyUnownedNonIgnoredBooksAsMissing()
    {
        var owned = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "The Final Empire", "Mistborn", "1", "Brandon Sanderson"),
            MakeDbAudiobook(2, "The Well of Ascension", "Mistborn", "2", "Brandon Sanderson"),
        };

        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(10, "The Final Empire", "1"),
                MakeExpected(11, "The Well of Ascension", "2"),
                MakeExpected(12, "The Hero of Ages", "3"),
                MakeExpected(13, "Secret History", "3.5", ignored: true),
            }
        };

        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(owned);
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(catalogRow);

        var detail = await MakeService().GetSeriesDetailAsync("Mistborn");

        Assert.IsNotNull(detail);
        Assert.AreEqual(2, detail.OwnedBooks.Count);
        Assert.AreEqual(1, detail.MissingBooks.Count);
        Assert.AreEqual("The Hero of Ages", detail.MissingBooks[0].Title);
        Assert.AreEqual(1, detail.IgnoredBooks.Count);
        Assert.AreEqual("Secret History", detail.IgnoredBooks[0].Title);
        Assert.AreEqual(1, detail.Overview.MissingBookCount);
        Assert.AreEqual(3, detail.Overview.ExpectedBookCount);
        Assert.IsTrue(detail.Overview.IsMatched);
    }

    [TestMethod]
    public async Task GetSeriesDetailAsync_TreatsFuzzilyMatchingTitleAsOwned()
    {
        // Position is absent on the owned book, so only the fuzzy title comparison can match it.
        var owned = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "The Hero of Ages", "Mistborn"),
        };

        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(10, "The Hero Of Ages.", "3"),
            }
        };

        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(owned);
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(catalogRow);

        var detail = await MakeService().GetSeriesDetailAsync("Mistborn");

        Assert.IsNotNull(detail);
        Assert.AreEqual(0, detail.MissingBooks.Count);
    }

    [TestMethod]
    public async Task GetSeriesDetailAsync_DoesNotTreatAMatchingPositionOnAWildlyDifferentTitleAsOwned()
    {
        // The source numbers a novella at 2.5 and the user typed "2.5" on an unrelated book:
        // position alone must not hide the genuinely missing entry.
        var owned = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "An Entirely Unrelated Story", "Mistborn", "2.5"),
        };

        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(10, "Secret History", "2.5") },
        };

        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(owned);
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(catalogRow);

        var detail = await MakeService().GetSeriesDetailAsync("Mistborn");

        Assert.IsNotNull(detail);
        Assert.AreEqual(1, detail.MissingBooks.Count);
        Assert.AreEqual("Secret History", detail.MissingBooks[0].Title);
    }

    [TestMethod]
    public async Task GetSeriesDetailAsync_TreatsMatchingPositionWithASimilarTitleAsOwned()
    {
        var owned = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Secret History (Unabridged)", "Mistborn", "2.5"),
        };

        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(10, "Secret History", "2.5") },
        };

        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(owned);
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(catalogRow);

        var detail = await MakeService().GetSeriesDetailAsync("Mistborn");

        Assert.IsNotNull(detail);
        Assert.AreEqual(0, detail.MissingBooks.Count);
    }

    [TestMethod]
    public async Task GetSeriesDetailAsync_TreatsAVerySimilarTitleAtTheWrongPositionAsOwned()
    {
        // The user typed the wrong part number; the title still identifies the book.
        var owned = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "The Hero of Ages", "Mistborn", "7"),
        };

        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(10, "The Hero of Ages", "3") },
        };

        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(owned);
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(catalogRow);

        var detail = await MakeService().GetSeriesDetailAsync("Mistborn");

        Assert.IsNotNull(detail);
        Assert.AreEqual(0, detail.MissingBooks.Count);
    }

    [TestMethod]
    public async Task IgnoreExpectedBookAsync_AddressesTheRowByItsNaturalKey()
    {
        _seriesRepository
            .Setup(r => r.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true))
            .Returns(Task.CompletedTask);

        await MakeService().IgnoreExpectedBookAsync("Mistborn", "3.5", "Secret History", true);

        _seriesRepository.Verify(
            r => r.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true), Times.Once);
    }

    [TestMethod]
    public async Task GetSeriesDetailAsync_ReturnsNullForUnknownSeries()
    {
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<DbAudiobook>());
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync(It.IsAny<string>()))
            .ReturnsAsync((Series?)null);

        Assert.IsNull(await MakeService().GetSeriesDetailAsync("Nonexistent"));
    }

    [TestMethod]
    public async Task GetAllSeriesOverviewAsync_MarksUnmatchedSeriesAndCountsOwnedBooks()
    {
        _audiobookRepository.Setup(r => r.GetSeriesGroupingDataAsync()).ReturnsAsync(new List<SeriesGroupingBook>
        {
            new("Series One", "1", "Book A", new List<string> { "Author X" }),
            new("Series One", "2", "Book B", new List<string> { "Author X" }),
        });
        _seriesRepository.Setup(r => r.GetAllWithExpectedBooksAsync()).ReturnsAsync(new List<Series>());

        var overviews = await MakeService().GetAllSeriesOverviewAsync();

        Assert.AreEqual(1, overviews.Count);
        Assert.AreEqual("Series One", overviews[0].Name);
        Assert.AreEqual(2, overviews[0].OwnedBookCount);
        Assert.IsFalse(overviews[0].IsMatched);
        Assert.AreEqual(0, overviews[0].MissingBookCount);
        CollectionAssert.AreEqual(new List<string> { "Author X" }, overviews[0].Authors);
    }

    [TestMethod]
    public async Task SuggestSeriesMatchesAsync_RanksCandidatesAndSkipsNonSeriesScrapers()
    {
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "The Final Empire", "Mistborn", "1", "Brandon Sanderson"),
        });

        var seriesScraper = new FakeSeriesScraper("Hardcover", new List<SeriesSearchResult>
        {
            new("7", "Mistborn Adjacent Companion"),
            new("42", "Mistborn") { Authors = new List<string> { "Brandon Sanderson" } },
        });

        var plainScraper = new Mock<IScraper>();
        plainScraper.SetupGet(s => s.SourceName).Returns("Goodreads");
        plainScraper.SetupGet(s => s.SupportsSeriesLookup).Returns(false);

        var candidates = await MakeService(seriesScraper, plainScraper.Object).SuggestSeriesMatchesAsync("Mistborn");

        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual("42", candidates[0].SourceId);
        Assert.IsTrue(candidates[0].Confidence > candidates[1].Confidence);
        Assert.AreEqual("Hardcover", candidates[0].SourceName);
        plainScraper.Verify(s => s.SearchSeries(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task MatchSeriesAsync_StoresRosterAndPreservesIgnoreFlags()
    {
        var existing = new Series
        {
            Id = 1,
            Name = "Mistborn",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(13, "Secret History", "3.5", ignored: true),
            }
        };

        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(existing);
        _seriesRepository.Setup(r => r.UpsertSeriesAsync(It.IsAny<Series>()))
            .ReturnsAsync((Series s) => { s.Id = 1; return s; });
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(new List<DbAudiobook>());

        List<SeriesExpectedBook>? stored = null;
        _seriesRepository
            .Setup(r => r.ReplaceExpectedBooksAsync(1, It.IsAny<List<SeriesExpectedBook>>()))
            .Callback((long _, List<SeriesExpectedBook> books) => stored = books)
            .Returns(Task.CompletedTask);

        var roster = new SeriesSearchResult("42", "Mistborn")
        {
            Books = new List<SeriesExpectedBookResult>
            {
                new("The Final Empire") { Position = "1" },
                new("Secret History") { Position = "3.5" },
            }
        };

        var scraper = new FakeSeriesScraper("Hardcover", new List<SeriesSearchResult>(), roster);

        await MakeService(scraper).MatchSeriesAsync("Mistborn", "Hardcover", "42", 0.95);

        Assert.IsNotNull(stored);
        Assert.AreEqual(2, stored.Count);
        Assert.IsFalse(stored.Single(b => b.Title == "The Final Empire").IsIgnored);
        Assert.IsTrue(stored.Single(b => b.Title == "Secret History").IsIgnored);

        _seriesRepository.Verify(r => r.UpsertSeriesAsync(It.Is<Series>(s =>
            s.MatchedSourceName == "Hardcover" &&
            s.MatchedSourceId == "42" &&
            s.MatchConfidence == 0.95 &&
            s.LastRefreshedAt != null)), Times.Once);
    }

    [TestMethod]
    public async Task BulkAutoMatchSeriesAsync_SkipsCandidatesBelowThresholdAndReportsProgress()
    {
        _audiobookRepository.Setup(r => r.GetSeriesGroupingDataAsync()).ReturnsAsync(new List<SeriesGroupingBook>
        {
            new("Mistborn", "1", "Book A", new List<string> { "Brandon Sanderson" }),
            new("Totally Different Value", "1", "Book B", new List<string> { "Someone Else" }),
        });
        _seriesRepository.Setup(r => r.GetAllWithExpectedBooksAsync()).ReturnsAsync(new List<Series>());
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<DbAudiobook>());
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync(It.IsAny<string>())).ReturnsAsync((Series?)null);
        _seriesRepository.Setup(r => r.UpsertSeriesAsync(It.IsAny<Series>()))
            .ReturnsAsync((Series s) => { s.Id = 1; return s; });
        _seriesRepository.Setup(r => r.ReplaceExpectedBooksAsync(It.IsAny<long>(), It.IsAny<List<SeriesExpectedBook>>()))
            .Returns(Task.CompletedTask);

        // Only "Mistborn" has a same-named candidate; the other series' only candidate scores far too low.
        var scraper = new FakeSeriesScraper(
            "Hardcover",
            new List<SeriesSearchResult> { new("42", "Mistborn") },
            new SeriesSearchResult("42", "Mistborn"));

        var progressCalls = new List<(int Processed, int Total, int Succeeded, int Failed)>();

        var (processed, succeeded, failed, stopReason) = await MakeService(scraper).BulkAutoMatchSeriesAsync(
            0.9,
            null,
            (p, t, s, f) => { progressCalls.Add((p, t, s, f)); return Task.CompletedTask; });

        Assert.AreEqual(2, processed);
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNull(stopReason);
        Assert.AreEqual(2, progressCalls.Count);
        Assert.AreEqual(2, progressCalls[^1].Total);
    }

    [TestMethod]
    public async Task BulkAutoMatchSeriesAsync_ContinuesAfterAFailingSeries()
    {
        _audiobookRepository.Setup(r => r.GetSeriesGroupingDataAsync()).ReturnsAsync(new List<SeriesGroupingBook>
        {
            new("Mistborn", "1", "Book A", new List<string>()),
            new("Mistborn Two", "1", "Book B", new List<string>()),
        });
        _seriesRepository.Setup(r => r.GetAllWithExpectedBooksAsync()).ReturnsAsync(new List<Series>());
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<DbAudiobook>());

        var scraper = new Mock<IScraper>();
        scraper.SetupGet(s => s.SourceName).Returns("Hardcover");
        scraper.SetupGet(s => s.SupportsSeriesLookup).Returns(true);
        scraper.SetupGet(s => s.RequiresApiKey).Returns(false);
        scraper.Setup(s => s.IsSource("Hardcover")).Returns(true);
        scraper.Setup(s => s.SearchSeries(It.IsAny<string>()))
            .ReturnsAsync((string term) => new List<SeriesSearchResult> { new("42", term) });
        scraper.Setup(s => s.GetSeriesBooks(It.IsAny<string>())).ThrowsAsync(new Exception("source exploded"));

        var (processed, succeeded, failed, stopReason) = await MakeService(scraper.Object)
            .BulkAutoMatchSeriesAsync(0.5, null, (_, _, _, _) => Task.CompletedTask);

        Assert.AreEqual(2, processed);
        Assert.AreEqual(0, succeeded);
        Assert.AreEqual(2, failed);
        Assert.IsNull(stopReason);
    }

    [TestMethod]
    public async Task BulkAutoMatchSeriesAsync_StopsImmediatelyWhenHardcoverDailyLimitIsExhausted()
    {
        _audiobookRepository.Setup(r => r.GetSeriesGroupingDataAsync()).ReturnsAsync(new List<SeriesGroupingBook>
        {
            new("Mistborn", "1", "Book A", new List<string>()),
            new("Mistborn Two", "1", "Book B", new List<string>()),
            new("Mistborn Three", "1", "Book C", new List<string>()),
        });
        _seriesRepository.Setup(r => r.GetAllWithExpectedBooksAsync()).ReturnsAsync(new List<Series>());
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<DbAudiobook>());
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync(It.IsAny<string>())).ReturnsAsync((Series?)null);
        _seriesRepository.Setup(r => r.UpsertSeriesAsync(It.IsAny<Series>()))
            .ReturnsAsync((Series s) => { s.Id = 1; return s; });
        _seriesRepository.Setup(r => r.ReplaceExpectedBooksAsync(It.IsAny<long>(), It.IsAny<List<SeriesExpectedBook>>()))
            .Returns(Task.CompletedTask);

        var scraper = new Mock<IScraper>();
        scraper.SetupGet(s => s.SourceName).Returns("Hardcover");
        scraper.SetupGet(s => s.SupportsSeriesLookup).Returns(true);
        scraper.SetupGet(s => s.RequiresApiKey).Returns(false);
        scraper.Setup(s => s.IsSource("Hardcover")).Returns(true);
        scraper.Setup(s => s.SearchSeries(It.IsAny<string>()))
            .ReturnsAsync((string term) => new List<SeriesSearchResult> { new("42", term) });
        // The daily budget runs out on the second series - the third must never be attempted.
        scraper.SetupSequence(s => s.GetSeriesBooks(It.IsAny<string>()))
            .ReturnsAsync(new SeriesSearchResult("42", "Mistborn"))
            .ThrowsAsync(new HardcoverDailyLimitExceededException(5000))
            .ThrowsAsync(new Exception("should never be reached"));

        var progressCalls = new List<(int Processed, int Total, int Succeeded, int Failed)>();

        var (processed, succeeded, failed, stopReason) = await MakeService(scraper.Object)
            .BulkAutoMatchSeriesAsync(0.5, null, (p, t, s, f) => { progressCalls.Add((p, t, s, f)); return Task.CompletedTask; });

        Assert.AreEqual(1, processed);
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNotNull(stopReason);
        Assert.AreEqual(1, progressCalls.Count);
        scraper.Verify(s => s.GetSeriesBooks(It.IsAny<string>()), Times.Exactly(2));
    }

    [TestMethod]
    public void ScoreCandidate_ScoresExactNameHighestAndUnrelatedNameLow()
    {
        var authors = new List<string> { "Brandon Sanderson" };

        var exact = SeriesService.ScoreCandidate("Mistborn", authors, "Mistborn", new List<string> { "Brandon Sanderson" });
        var punctuation = SeriesService.ScoreCandidate("Mistborn", authors, "Mistborn.", new List<string>());
        var unrelated = SeriesService.ScoreCandidate("Mistborn", authors, "The Wheel of Time", new List<string>());

        Assert.AreEqual(1.0, exact, 0.0001);
        Assert.IsTrue(punctuation > 0.9, $"expected near-identical name to score high, got {punctuation}");
        Assert.IsTrue(unrelated < 0.5, $"expected unrelated name to score low, got {unrelated}");
    }

    [TestMethod]
    public void ScoreCandidate_AuthorOverlapRaisesScoreButNeverRescuesAWrongName()
    {
        var authors = new List<string> { "Brandon Sanderson" };

        var withAuthor = SeriesService.ScoreCandidate("Mistborn Saga", authors, "Mistborn Sage", new List<string> { "Brandon Sanderson" });
        var withoutAuthor = SeriesService.ScoreCandidate("Mistborn Saga", authors, "Mistborn Sage", new List<string> { "Someone Else" });
        var wrongName = SeriesService.ScoreCandidate("Mistborn", authors, "The Wheel of Time", new List<string> { "Brandon Sanderson" });

        Assert.IsTrue(withAuthor > withoutAuthor);
        Assert.IsTrue(wrongName < 0.6, $"author overlap should not rescue an unrelated name, got {wrongName}");
    }

    /// <summary>
    /// Minimal series-capable scraper so the tests exercise the default IScraper capability
    /// members rather than mocking every book-scraping method.
    /// </summary>
    private class FakeSeriesScraper : IScraper
    {
        private readonly IList<SeriesSearchResult> _searchResults;
        private readonly SeriesSearchResult? _roster;

        public FakeSeriesScraper(string sourceName, IList<SeriesSearchResult> searchResults, SeriesSearchResult? roster = null)
        {
            SourceName = sourceName;
            _searchResults = searchResults;
            _roster = roster;
        }

        public string SourceName { get; }

        public bool SupportsSeriesLookup => true;

        public bool IsSource(string sourceName) =>
            string.Equals(sourceName, SourceName, StringComparison.InvariantCultureIgnoreCase);

        public bool SupportsUrl(string url) => false;

        public Task<IList<BookSearchResult>> Search(string searchTerm) =>
            Task.FromResult<IList<BookSearchResult>>(new List<BookSearchResult>());

        public Task<BookSearchResult> GetBookDetails(string bookUrl) => throw new NotImplementedException();

        public Task<IList<SeriesSearchResult>> SearchSeries(string searchTerm) => Task.FromResult(_searchResults);

        public Task<SeriesSearchResult?> GetSeriesBooks(string seriesIdOrUrl) => Task.FromResult(_roster);
    }
}
