using AudiobookManager.Domain;
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
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class SeriesServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<ISeriesRepository> _seriesRepository = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<ILogger<SeriesService>> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _seriesRepository = new Mock<ISeriesRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<SeriesService>>();
    }

    private SeriesService MakeService(params IScraper[] scrapers) =>
        new(_audiobookRepository.Object, _seriesRepository.Object, _audiobookService.Object, scrapers, _logger.Object);

    private static DbAudiobook MakeDbAudiobook(
        long id,
        string bookName,
        string? series = null,
        string? seriesPart = null,
        string? author = null)
    {
        var book = new DbAudiobook(id, bookName, null, series, seriesPart, 2024, null, null, null, null, null, null, null,
            null, null, $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);

        if (author is not null)
        {
            book.Authors.Add(new DbPerson(id, author));
        }

        return book;
    }

    private static SeriesExpectedBook MakeExpected(long id, string title, string? position, bool ignored = false) =>
        new() { Id = id, SeriesId = 1, Title = title, Position = position, IsIgnored = ignored };

    // Regression test: a roster entry with no position must still be matched on title against
    // owned books that *do* have one. IsSameBook falls back to a fuzzy title comparison whenever
    // the positions don't settle it, so partitioning the owned books by position must not stop
    // those pairs from ever being compared - or a book the user owns is reported missing.
    [TestMethod]
    public async Task GetSeriesDetailAsync_ExpectedBookWithNoPosition_MatchesAnOwnedBookThatHasOne()
    {
        var owned = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "The Final Empire", "Mistborn", "1", "Brandon Sanderson"),
        };

        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                // The source lists it without a position at all.
                MakeExpected(10, "The Final Empire", null),
            },
        };

        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Mistborn", null)).ReturnsAsync(owned);
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(catalogRow);

        var detail = await MakeService().GetSeriesDetailAsync("Mistborn");

        Assert.IsNotNull(detail);
        CollectionAssert.AreEqual(
            new List<string>(),
            detail.MissingBooks.Select(b => b.Title).ToList(),
            "the owned book matches the positionless roster entry on title");
        Assert.AreEqual(0, detail.Overview.MissingBookCount);
    }

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
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn")).ReturnsAsync(new List<string>
        {
            "Brandon Sanderson",
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
    public async Task SearchSeriesMatchesAsync_WithPlainTextQuery_SearchesByQueryNotLibrarySeriesName()
    {
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn")).ReturnsAsync(new List<string>
        {
            "Brandon Sanderson",
        });

        var scraper = new FakeSeriesScraper("Hardcover", new List<SeriesSearchResult>
        {
            new("42", "Mistborn") { Authors = new List<string> { "Brandon Sanderson" } },
        });

        var candidates = await MakeService(scraper).SearchSeriesMatchesAsync("Mistborn", "Alloy of Law");

        Assert.AreEqual("Alloy of Law", scraper.LastSearchTerm);
        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual("42", candidates[0].SourceId);
    }

    [TestMethod]
    public async Task SearchSeriesMatchesAsync_WithUrl_ReturnsSingleCandidateFromMatchingScraper()
    {
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn")).ReturnsAsync(new List<string>
        {
            "Brandon Sanderson",
        });

        var roster = new SeriesSearchResult("42", "Mistborn") { Authors = new List<string> { "Brandon Sanderson" } };
        var matchingScraper = new FakeSeriesScraper(
            "Hardcover",
            new List<SeriesSearchResult>(),
            roster,
            url => url.Contains("hardcover.app"));
        var otherScraper = new Mock<IScraper>();
        otherScraper.SetupGet(s => s.SourceName).Returns("Goodreads");
        otherScraper.SetupGet(s => s.SupportsSeriesLookup).Returns(false);

        var candidates = await MakeService(matchingScraper, otherScraper.Object)
            .SearchSeriesMatchesAsync("Mistborn", "https://hardcover.app/series/mistborn");

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual("Hardcover", candidates[0].SourceName);
        Assert.AreEqual("42", candidates[0].SourceId);
        otherScraper.Verify(s => s.GetSeriesBooks(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task SearchSeriesMatchesAsync_WithUrlNoScraperSupportsIt_ReturnsEmpty()
    {
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn")).ReturnsAsync(new List<string>());

        var scraper = new FakeSeriesScraper("Hardcover", new List<SeriesSearchResult>());

        var candidates = await MakeService(scraper)
            .SearchSeriesMatchesAsync("Mistborn", "https://example.com/series/mistborn");

        Assert.AreEqual(0, candidates.Count);
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
    public async Task MatchSeriesAsync_StoresFullRosterIncludingOmnibusEditionsRegardlessOfSetting()
    {
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Thursday Murder Club")).ReturnsAsync((Series?)null);
        _seriesRepository.Setup(r => r.UpsertSeriesAsync(It.IsAny<Series>()))
            .ReturnsAsync((Series s) => { s.Id = 1; return s; });
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Thursday Murder Club", null)).ReturnsAsync(new List<DbAudiobook>());

        List<SeriesExpectedBook>? stored = null;
        _seriesRepository
            .Setup(r => r.ReplaceExpectedBooksAsync(1, It.IsAny<List<SeriesExpectedBook>>()))
            .Callback((long _, List<SeriesExpectedBook> books) => stored = books)
            .Returns(Task.CompletedTask);

        var roster = new SeriesSearchResult("99", "Thursday Murder Club")
        {
            Books = new List<SeriesExpectedBookResult>
            {
                new("The Thursday Murder Club") { Position = "1" },
                new("The Thursday Murder Club / The Man Who Died Twice") { Position = "1", IsCompilation = true },
            }
        };

        var scraper = new FakeSeriesScraper("Hardcover", new List<SeriesSearchResult>(), roster);

        // includeOmnibusEditions defaults to false here, but storage keeps every roster entry
        // regardless - the setting only affects what's shown when reading it back.
        await MakeService(scraper).MatchSeriesAsync("Thursday Murder Club", "Hardcover", "99");

        Assert.IsNotNull(stored);
        Assert.AreEqual(2, stored.Count);
        Assert.IsFalse(stored.Single(b => b.Title == "The Thursday Murder Club").IsCompilation);
        Assert.IsTrue(stored.Single(b => b.Title == "The Thursday Murder Club / The Man Who Died Twice").IsCompilation);

        _seriesRepository.Verify(r => r.UpsertSeriesAsync(It.Is<Series>(s => !s.IncludeOmnibusEditions)), Times.Once);
    }

    [TestMethod]
    public async Task GetSeriesDetailAsync_HidesCompilationsUnlessIncludeOmnibusEditionsIsSet()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Thursday Murder Club",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "99",
            IncludeOmnibusEditions = false,
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(1, "The Thursday Murder Club", "1"),
                new()
                {
                    Id = 2,
                    Title = "The Thursday Murder Club / The Man Who Died Twice",
                    Position = "1",
                    IsCompilation = true,
                },
            },
        };

        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Thursday Murder Club")).ReturnsAsync(catalogRow);
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Thursday Murder Club", null)).ReturnsAsync(new List<DbAudiobook>());

        var detail = await MakeService().GetSeriesDetailAsync("Thursday Murder Club");

        Assert.IsNotNull(detail);
        Assert.AreEqual(1, detail.MissingBooks.Count);
        Assert.AreEqual("The Thursday Murder Club", detail.MissingBooks.Single().Title);
        Assert.AreEqual(1, detail.Overview.MissingBookCount);

        catalogRow.IncludeOmnibusEditions = true;

        var detailWithOmnibus = await MakeService().GetSeriesDetailAsync("Thursday Murder Club");

        Assert.IsNotNull(detailWithOmnibus);
        Assert.AreEqual(2, detailWithOmnibus.MissingBooks.Count);
        Assert.AreEqual(2, detailWithOmnibus.Overview.MissingBookCount);
    }

    [TestMethod]
    public async Task SetIncludeOmnibusEditionsAsync_UpdatesFlagWithoutRefetchingRoster()
    {
        var existing = new Series
        {
            Id = 1,
            Name = "Thursday Murder Club",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "99",
            ExpectedBooks = new List<SeriesExpectedBook>(),
        };

        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Thursday Murder Club")).ReturnsAsync(existing);
        _seriesRepository.Setup(r => r.SetIncludeOmnibusEditionsAsync("Thursday Murder Club", true))
            .Callback(() => existing.IncludeOmnibusEditions = true)
            .ReturnsAsync(existing);
        _audiobookRepository.Setup(r => r.GetBooksBySeriesAsync("Thursday Murder Club", null)).ReturnsAsync(new List<DbAudiobook>());

        // No scraper is registered at all - if the service tried to re-fetch the roster, this
        // would throw for lacking a series-capable source.
        var overview = await MakeService().SetIncludeOmnibusEditionsAsync("Thursday Murder Club", true);

        Assert.IsTrue(overview.IncludeOmnibusEditions);
        _seriesRepository.Verify(r => r.SetIncludeOmnibusEditionsAsync("Thursday Murder Club", true), Times.Once);
        _seriesRepository.Verify(r => r.UpsertSeriesAsync(It.IsAny<Series>()), Times.Never);
        _seriesRepository.Verify(r => r.ReplaceExpectedBooksAsync(It.IsAny<long>(), It.IsAny<List<SeriesExpectedBook>>()), Times.Never);
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

    // Regression test: RefreshManyAsync read the series row to check it was matched, then
    // MatchSeriesCoreAsync immediately read the very same row again to carry the ignore flags
    // across - so refreshing N series cost 2N reads, each pulling a full roster. Fails against
    // the pre-fix service, which reads it twice.
    [TestMethod]
    public async Task RefreshSeriesAsync_ReadsTheSeriesRowOnce()
    {
        var existing = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>(),
        };

        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(existing);
        _seriesRepository.Setup(r => r.UpsertSeriesAsync(It.IsAny<Series>()))
            .ReturnsAsync((Series row) => { row.Id = 1; return row; });
        _seriesRepository.Setup(r => r.ReplaceExpectedBooksAsync(It.IsAny<long>(), It.IsAny<List<SeriesExpectedBook>>()))
            .Returns(Task.CompletedTask);

        var scraper = new Mock<IScraper>();
        scraper.SetupGet(s => s.SourceName).Returns("Hardcover");
        scraper.SetupGet(s => s.SupportsSeriesLookup).Returns(true);
        scraper.SetupGet(s => s.RequiresApiKey).Returns(false);
        scraper.Setup(s => s.IsSource("Hardcover")).Returns(true);
        scraper.Setup(s => s.GetSeriesBooks(It.IsAny<string>()))
            .ReturnsAsync(new SeriesSearchResult("42", "Mistborn"));

        var (processed, succeeded, failed, _) = await MakeService(scraper.Object)
            .RefreshSeriesAsync("Mistborn", (_, _, _, _) => Task.CompletedTask);

        Assert.AreEqual(1, processed);
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(0, failed);
        _seriesRepository.Verify(r => r.GetByNameWithExpectedBooksAsync("Mistborn"), Times.Once);
    }

    // The ignore flags a refresh carries across still have to survive the row being passed in
    // rather than re-read - guards against "fixing" the double read by dropping the roster.
    [TestMethod]
    public async Task RefreshSeriesAsync_CarriesPreviouslyIgnoredEntriesAcross()
    {
        var existing = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(1, "Secret History", "3.5", ignored: true),
            },
        };

        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksAsync("Mistborn")).ReturnsAsync(existing);
        _seriesRepository.Setup(r => r.UpsertSeriesAsync(It.IsAny<Series>()))
            .ReturnsAsync((Series row) => { row.Id = 1; return row; });

        List<SeriesExpectedBook> replaced = new();
        _seriesRepository.Setup(r => r.ReplaceExpectedBooksAsync(It.IsAny<long>(), It.IsAny<List<SeriesExpectedBook>>()))
            .Callback((long _, List<SeriesExpectedBook> books) => replaced = books)
            .Returns(Task.CompletedTask);

        var roster = new SeriesSearchResult("42", "Mistborn")
        {
            Books = new List<SeriesExpectedBookResult>
            {
                new("The Final Empire") { Position = "1" },
                new("Secret History") { Position = "3.5" },
            },
        };

        var scraper = new Mock<IScraper>();
        scraper.SetupGet(s => s.SourceName).Returns("Hardcover");
        scraper.SetupGet(s => s.SupportsSeriesLookup).Returns(true);
        scraper.SetupGet(s => s.RequiresApiKey).Returns(false);
        scraper.Setup(s => s.IsSource("Hardcover")).Returns(true);
        scraper.Setup(s => s.GetSeriesBooks(It.IsAny<string>())).ReturnsAsync(roster);

        await MakeService(scraper.Object).RefreshSeriesAsync("Mistborn", (_, _, _, _) => Task.CompletedTask);

        Assert.AreEqual(2, replaced.Count);
        Assert.IsFalse(replaced.Single(b => b.Title == "The Final Empire").IsIgnored);
        Assert.IsTrue(replaced.Single(b => b.Title == "Secret History").IsIgnored);
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

    [TestMethod]
    public async Task FindMissingBookCandidatesAsync_RanksAuthorAndTitleMatchAheadOfBetterTitleOnlyMatch()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookAsync("Mistborn", "3", "The Hero of Ages"))
            .ReturnsAsync(MakeExpected(12, "The Hero of Ages", "3"));
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn")).ReturnsAsync(new List<string>
        {
            "Brandon Sanderson",
        });
        _audiobookRepository.Setup(r => r.GetSeriesCandidateDataAsync("The Hero of Ages", 1000)).ReturnsAsync(new List<SeriesCandidateBook>
        {
            // Exact title, wrong author: the best tier-2 candidate.
            new(2, "The Hero of Ages", null, null, 2010, new List<string> { "Someone Entirely Unrelated" }),
            // Weaker title, right author: must rank ahead despite the lower title score.
            new(3, "Hero of Ages", null, null, 2010, new List<string> { "Brandon Sanderson" }),
            // Below the title threshold entirely.
            new(4, "A Completely Different Book", null, null, 2001, new List<string> { "Brandon Sanderson" }),
        });

        var result = await MakeService().FindMissingBookCandidatesAsync("Mistborn", "3", "The Hero of Ages");

        Assert.AreEqual(2, result.Count, "the below-threshold book must be excluded");
        Assert.AreEqual("Hero of Ages", result[0].BookName, "the author+title match outranks the better title-only match");
        Assert.IsTrue(result[0].AuthorMatches);
        Assert.AreEqual(0.75, result[0].TitleSimilarity);
        Assert.AreEqual("The Hero of Ages", result[1].BookName);
        Assert.IsFalse(result[1].AuthorMatches);
        Assert.AreEqual(1.0, result[1].TitleSimilarity);
        _audiobookRepository.Verify(r => r.GetSeriesCandidateDataAsync("The Hero of Ages", 1000), Times.Once);
    }

    [TestMethod]
    public async Task FindMissingBookCandidatesAsync_AuthorMatchIsAccentAndInitialsTolerant()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookAsync("Wizard School", "1", "The First Book"))
            .ReturnsAsync(MakeExpected(21, "The First Book", "1"));
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Wizard School")).ReturnsAsync(new List<string>
        {
            "J.K. Rowling",
        });
        _audiobookRepository.Setup(r => r.GetSeriesCandidateDataAsync("The First Book", 1000)).ReturnsAsync(new List<SeriesCandidateBook>
        {
            new(2, "The First Book", null, null, 1997, new List<string> { "JK Rowling" }),
            new(3, "The First Book", null, null, 1997, new List<string> { "Author McAuthorface" }),
        });

        var result = await MakeService().FindMissingBookCandidatesAsync("Wizard School", "1", "The First Book");

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Single(c => c.AudiobookId == 2).AuthorMatches, "'JK Rowling' normalizes equal to 'J.K. Rowling'");
        Assert.IsFalse(result.Single(c => c.AudiobookId == 3).AuthorMatches);
    }

    [TestMethod]
    public async Task FindMissingBookCandidatesAsync_CapsTheResultList()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookAsync("Mistborn", "3", "The Hero of Ages"))
            .ReturnsAsync(MakeExpected(12, "The Hero of Ages", "3"));
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn")).ReturnsAsync(new List<string>
        {
            "Brandon Sanderson",
        });
        var many = Enumerable.Range(1, SeriesService.MaxMissingBookCandidates + 5)
            .Select(i => new SeriesCandidateBook(i, "The Hero of Ages", null, null, 2010, new List<string>()))
            .ToList();
        _audiobookRepository.Setup(r => r.GetSeriesCandidateDataAsync("The Hero of Ages", 1000)).ReturnsAsync(many);

        var result = await MakeService().FindMissingBookCandidatesAsync("Mistborn", "3", "The Hero of Ages");

        Assert.AreEqual(SeriesService.MaxMissingBookCandidates, result.Count);
    }

    [TestMethod]
    public async Task FindMissingBookCandidatesAsync_UnknownExpectedBook_Throws()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookAsync("Mistborn", "9", "Nope")).ReturnsAsync((SeriesExpectedBook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            MakeService().FindMissingBookCandidatesAsync("Mistborn", "9", "Nope"));
    }

    private static DomainAudiobook MakeDomainAudiobook(long id) => new(
        new List<DomainPerson> { new("Brandon Sanderson") },
        "The Hero of Ages",
        2010,
        new AudiobookFileInfo("/library/the-hero-of-ages.m4b", "the-hero-of-ages.m4b", 1000))
    {
        Id = id,
        Description = "Original description",
        Series = "Some Other Series",
        SeriesPart = "7",
    };

    [TestMethod]
    public async Task ApplyMissingBookAsync_AppliesSeriesAndPartAndPreservesEverythingElse()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookStrictAsync("Mistborn", "3", "The Hero of Ages"))
            .ReturnsAsync(MakeExpected(12, "The Hero of Ages", "3"));
        var book = MakeDomainAudiobook(5);
        _audiobookService.Setup(s => s.GetAudiobookById(5)).ReturnsAsync(book);

        DomainAudiobook? captured = null;
        _audiobookService
            .Setup(s => s.UpdateAudiobook(5, It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Callback<long, DomainAudiobook, Func<string, int, Task>>((_, b, _) => captured = b)
            .ReturnsAsync(book);

        await MakeService().ApplyMissingBookAsync("Mistborn", "3", "The Hero of Ages", 5);

        Assert.IsNotNull(captured);
        Assert.AreEqual("Mistborn", captured!.Series);
        Assert.AreEqual("3", captured.SeriesPart);
        Assert.AreEqual("The Hero of Ages", captured.BookName, "the book keeps its own name");
        Assert.AreEqual("Original description", captured.Description);
        Assert.AreEqual("Brandon Sanderson", captured.Authors.Single().Name);
        Assert.AreEqual(2010, captured.Year);
        Assert.AreEqual("/library/the-hero-of-ages.m4b", captured.FileInfo.FullPath);
        _audiobookService.Verify(
            s => s.UpdateAudiobook(5, It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()), Times.Once);
    }

    [TestMethod]
    public async Task ApplyMissingBookAsync_PositionlessRosterEntry_ClearsThePart()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookStrictAsync("Mistborn", null, "Secret History"))
            .ReturnsAsync(MakeExpected(13, "Secret History", null));
        var book = MakeDomainAudiobook(5);
        _audiobookService.Setup(s => s.GetAudiobookById(5)).ReturnsAsync(book);

        DomainAudiobook? captured = null;
        _audiobookService
            .Setup(s => s.UpdateAudiobook(5, It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Callback<long, DomainAudiobook, Func<string, int, Task>>((_, b, _) => captured = b)
            .ReturnsAsync(book);

        await MakeService().ApplyMissingBookAsync("Mistborn", null, "Secret History", 5);

        Assert.IsNotNull(captured);
        Assert.AreEqual("Mistborn", captured!.Series);
        Assert.IsNull(captured.SeriesPart, "a roster entry with no position clears the part");
    }

    [TestMethod]
    public async Task ApplyMissingBookAsync_UnknownAudiobook_Throws()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookStrictAsync("Mistborn", "3", "The Hero of Ages"))
            .ReturnsAsync(MakeExpected(12, "The Hero of Ages", "3"));
        _audiobookService.Setup(s => s.GetAudiobookById(999)).ReturnsAsync((DomainAudiobook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            MakeService().ApplyMissingBookAsync("Mistborn", "3", "The Hero of Ages", 999));
    }

    [TestMethod]
    public async Task ApplyMissingBookAsync_UnknownExpectedBook_Throws()
    {
        _seriesRepository.Setup(r => r.FindExpectedBookStrictAsync("Mistborn", "9", "Nope")).ReturnsAsync((SeriesExpectedBook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            MakeService().ApplyMissingBookAsync("Mistborn", "9", "Nope", 5));
    }

    [TestMethod]
    public async Task ApplyMissingBookAsync_ConflictingPositionAndTitle_ThrowsAndDoesNotUpdate()
    {
        // Position "3" maps to "The Hero of Ages", title "Secret History" maps to position "3.5".
        // Strict matching requires both to match the same row — so this fails.
        _seriesRepository
            .Setup(r => r.FindExpectedBookStrictAsync("Mistborn", "3", "Secret History"))
            .ReturnsAsync((SeriesExpectedBook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            MakeService().ApplyMissingBookAsync("Mistborn", "3", "Secret History", 5));

        _audiobookService.Verify(s => s.GetAudiobookById(It.IsAny<long>()), Times.Never);
        _audiobookService.Verify(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()), Times.Never);
    }

    /// <summary>
    /// Minimal series-capable scraper so the tests exercise the default IScraper capability
    /// members rather than mocking every book-scraping method.
    /// </summary>
    private class FakeSeriesScraper : IScraper
    {
        private readonly IList<SeriesSearchResult> _searchResults;
        private readonly SeriesSearchResult? _roster;
        private readonly Func<string, bool> _supportsUrl;

        public string? LastSearchTerm { get; private set; }

        public FakeSeriesScraper(
            string sourceName,
            IList<SeriesSearchResult> searchResults,
            SeriesSearchResult? roster = null,
            Func<string, bool>? supportsUrl = null)
        {
            SourceName = sourceName;
            _searchResults = searchResults;
            _roster = roster;
            _supportsUrl = supportsUrl ?? (_ => false);
        }

        public string SourceName { get; }

        public bool SupportsSeriesLookup => true;

        public bool IsSource(string sourceName) =>
            string.Equals(sourceName, SourceName, StringComparison.InvariantCultureIgnoreCase);

        public bool SupportsUrl(string url) => _supportsUrl(url);

        public Task<IList<MetadataSearchResult>> Search(string searchTerm) =>
            Task.FromResult<IList<MetadataSearchResult>>(new List<MetadataSearchResult>());

        public Task<MetadataSearchResult> GetBookDetails(string bookUrl) => throw new NotImplementedException();

        public Task<IList<SeriesSearchResult>> SearchSeries(string searchTerm)
        {
            LastSearchTerm = searchTerm;
            return Task.FromResult(_searchResults);
        }

        public Task<SeriesSearchResult?> GetSeriesBooks(string seriesIdOrUrl) => Task.FromResult(_roster);
    }
}
