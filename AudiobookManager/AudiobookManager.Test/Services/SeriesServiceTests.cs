using AudiobookManager.Domain;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
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
    private SeriesReconciliationCache _reconciliationCache = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _seriesRepository = new Mock<ISeriesRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<SeriesService>>();
        _reconciliationCache = new SeriesReconciliationCache();
    }

    private SeriesService MakeService(params IScraper[] scrapers) =>
        new(_audiobookRepository.Object, _seriesRepository.Object, _audiobookService.Object, _reconciliationCache, scrapers, _logger.Object);

    private static SeriesExpectedBook MakeExpected(long id, string title, string? position, bool ignored = false) =>
        new() { Id = id, SeriesId = 1, Title = title, Position = position, IsIgnored = ignored };

    private static SeriesGroupingBook MakeGrouping(string series, string? part, string bookName, string? author = "Brandon Sanderson") =>
        new(series, part, bookName, author is null ? new List<string>() : new List<string> { author });

    private static List<SeriesOwnedBookRow> ToOwnedRows(List<SeriesGroupingBook> grouping) =>
        grouping
            .Select((b, i) => new SeriesOwnedBookRow(i + 1, b.BookName, b.SeriesPart, 2024, b.Authors, new List<string>(), null, null))
            .ToList();

    /// <summary>
    /// Stubs every read the paged detail makes for one series: the bound <c>GetByNameAsync</c>
    /// catalog metadata and the SQL owned page (rendered rows) on every request, plus - for the
    /// one-time reconciliation refill - the bounded roster read
    /// (<c>GetByNameWithExpectedBooksBoundedAsync</c>), the bounded owned keys
    /// (<c>GetSeriesOwnedKeysAsync</c>) and the author names. All the reconciliation inputs are
    /// cheap projections that report no overflow; tests that need a distinct owned page, slice or
    /// overflow scenario stub those themselves on top of this.
    /// </summary>
    private void StubSeries(string seriesName, List<SeriesGroupingBook> grouping, Series? catalogRow = null)
    {
        _seriesRepository.Setup(r => r.GetByNameAsync(seriesName)).ReturnsAsync(catalogRow);
        _seriesRepository
            .Setup(r => r.GetByNameWithExpectedBooksBoundedAsync(seriesName, It.IsAny<int>()))
            .ReturnsAsync((catalogRow, Overflow: false));
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedBooksPageAsync(seriesName, 0, 100))
            .ReturnsAsync((ToOwnedRows(grouping), grouping.Count));
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedKeysAsync(seriesName, It.IsAny<int>()))
            .ReturnsAsync((
                grouping.Select((b, i) => new SeriesOwnedKey(i + 1, b.SeriesPart, b.BookName)).ToList(),
                Overflow: false));
        _audiobookRepository
            .Setup(r => r.GetAuthorNamesBySeriesAsync(seriesName))
            .ReturnsAsync(
                grouping.SelectMany(b => b.Authors)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private Task<SeriesDetailPage?> GetDetailPageAsync(
        string seriesName,
        int ownedSkip = 0, int ownedTake = 100,
        int missingSkip = 0, int missingTake = 100,
        int ignoredSkip = 0, int ignoredTake = 100,
        int partMismatchSkip = 0, int partMismatchTake = 100) =>
        MakeService().GetSeriesDetailPageAsync(
            seriesName, ownedSkip, ownedTake, missingSkip, missingTake, ignoredSkip, ignoredTake,
            partMismatchSkip, partMismatchTake);

    // Regression test: a roster entry with no position must still be matched on title against
    // owned books that *do* have one. IsSameBook falls back to a fuzzy title comparison whenever
    // the positions don't settle it, so partitioning the owned books by position must not stop
    // those pairs from ever being compared - or a book the user owns is reported missing.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_ExpectedBookWithNoPosition_MatchesAnOwnedBookThatHasOne()
    {
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

        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", "1", "The Final Empire") }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        CollectionAssert.AreEqual(
            new List<string>(),
            page.MissingBooks.Select(b => b.Title).ToList(),
            "the owned book matches the positionless roster entry on title");
        Assert.AreEqual(0, page.Overview.MissingBookCount);
    }

    [TestMethod]
    public async Task GetSeriesDetailPageAsync_ReportsOnlyUnownedNonIgnoredBooksAsMissing()
    {
        var grouping = new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "1", "The Final Empire"),
            MakeGrouping("Mistborn", "2", "The Well of Ascension"),
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

        StubSeries("Mistborn", grouping, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(2, page.OwnedBookTotal);
        Assert.AreEqual(2, page.OwnedBooks.Count);
        Assert.AreEqual(1, page.MissingBooks.Count);
        Assert.AreEqual("The Hero of Ages", page.MissingBooks[0].Title);
        Assert.AreEqual(1, page.IgnoredBooks.Count);
        Assert.AreEqual("Secret History", page.IgnoredBooks[0].Title);
        Assert.AreEqual(1, page.Overview.MissingBookCount);
        Assert.AreEqual(3, page.Overview.ExpectedBookCount);
        Assert.IsTrue(page.Overview.IsMatched);
    }

    [TestMethod]
    public async Task GetSeriesDetailPageAsync_TreatsFuzzilyMatchingTitleAsOwned()
    {
        // Position is absent on the owned book, so only the fuzzy title comparison can match it.
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

        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", null, "The Hero of Ages") }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.MissingBooks.Count);
    }

    [TestMethod]
    public async Task GetSeriesDetailPageAsync_DoesNotTreatAMatchingPositionOnAWildlyDifferentTitleAsOwned()
    {
        // The source numbers a novella at 2.5 and the user typed "2.5" on an unrelated book:
        // position alone must not hide the genuinely missing entry.
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(10, "Secret History", "2.5") },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", "2.5", "An Entirely Unrelated Story") }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.MissingBooks.Count);
        Assert.AreEqual("Secret History", page.MissingBooks[0].Title);
    }

    [TestMethod]
    public async Task GetSeriesDetailPageAsync_TreatsMatchingPositionWithASimilarTitleAsOwned()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(10, "Secret History", "2.5") },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", "2.5", "Secret History (Unabridged)") }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.MissingBooks.Count);
    }

    [TestMethod]
    public async Task GetSeriesDetailPageAsync_TreatsAVerySimilarTitleAtTheWrongPositionAsOwned()
    {
        // The user typed the wrong part number; the title still identifies the book.
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(10, "The Hero of Ages", "3") },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", "7", "The Hero of Ages") }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.MissingBooks.Count);
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
    public async Task GetSeriesDetailPageAsync_ReturnsNullForUnknownSeries()
    {
        _seriesRepository.Setup(r => r.GetByNameAsync("Nonexistent")).ReturnsAsync((Series?)null);
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedBooksPageAsync("Nonexistent", 0, 100))
            .ReturnsAsync((new List<SeriesOwnedBookRow>(), 0));

        Assert.IsNull(await GetDetailPageAsync("Nonexistent"));
    }

    // The owned section is a SQL page; the slice parameters must reach the repository untouched
    // so the page is sliced in SQL, not after materializing the series' whole owned projection.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_PassesTheOwnedSliceToTheRepository()
    {
        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", "1", "Book One") }, catalogRow: null);
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedBooksPageAsync("Mistborn", 40, 25))
            .ReturnsAsync((new List<SeriesOwnedBookRow>
            {
                new(1, "Book One", "1", 2024, new List<string> { "Brandon Sanderson" }, new List<string>(), null, null),
            }, 27));

        var page = await GetDetailPageAsync("Mistborn", ownedSkip: 40, ownedTake: 25);

        Assert.IsNotNull(page);
        Assert.AreEqual(27, page.OwnedBookTotal, "the total is the series' full owned count, not the slice");
        Assert.AreEqual(1, page.OwnedBooks.Count);
        _audiobookRepository.Verify(r => r.GetSeriesOwnedBooksPageAsync("Mistborn", 40, 25), Times.Once);
    }

    // Regression: the owned section used to drop the cover path when mapping the SQL projection
    // (SeriesOwnedBookRow) to the domain owned book, so the series view's rows never rendered a
    // cover. The row's path must be copied through to the page the service returns.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_CopiesTheCoverFilePathFromTheOwnedRow()
    {
        const string coverPath = "/library/Brandon Sanderson/Mistborn/2006 - The Final Empire/cover.jpg";
        StubSeries("Mistborn", new List<SeriesGroupingBook> { MakeGrouping("Mistborn", "1", "The Final Empire") }, catalogRow: null);
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedBooksPageAsync("Mistborn", 0, 100))
            .ReturnsAsync((new List<SeriesOwnedBookRow>
            {
                new(1, "The Final Empire", "1", 2006, new List<string> { "Brandon Sanderson" }, new List<string>(), 88000, coverPath),
            }, 1));

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.OwnedBooks.Count);
        Assert.AreEqual(coverPath, page.OwnedBooks[0].CoverFilePath);
    }

    // The missing section is derived from the roster (bounded by the source's series page) plus
    // the series' owned keys and sliced in the service; the slice boundaries must stay stable so
    // every missing entry appears on exactly one page.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_PagesTheMissingSection_NoBookSkippedOrRepeated()
    {
        var roster = Enumerable.Range(1, 23)
            .Select(i => MakeExpected(i, $"Expected Book {i:00}", (i % 7).ToString()))
            .ToList();
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = roster,
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>(), catalogRow);

        var seen = new List<string>();
        for (var skip = 0; skip < 23; skip += 10)
        {
            var page = await GetDetailPageAsync("Mistborn", missingSkip: skip, missingTake: 10);
            seen.AddRange(page!.MissingBooks.Select(b => b.Title));
        }

        Assert.AreEqual(23, seen.Count, "no missing book may be dropped by paging");
        Assert.AreEqual(23, seen.Distinct().Count(), "no missing book may appear on two pages");
    }

    [TestMethod]
    public async Task GetSeriesDetailPageAsync_PagesTheIgnoredSection_NoBookSkippedOrRepeated()
    {
        var roster = Enumerable.Range(1, 23)
            .Select(i => MakeExpected(i, $"Ignored Book {i:00}", (i % 7).ToString(), ignored: true))
            .ToList();
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = roster,
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>(), catalogRow);

        var seen = new List<string>();
        for (var skip = 0; skip < 23; skip += 10)
        {
            var page = await GetDetailPageAsync("Mistborn", ignoredSkip: skip, ignoredTake: 10);
            seen.AddRange(page!.IgnoredBooks.Select(b => b.Title));
        }

        Assert.AreEqual(23, seen.Count, "no ignored book may be dropped by paging");
        Assert.AreEqual(23, seen.Distinct().Count(), "no ignored book may appear on two pages");
    }

    // The point of the reconciliation cache: many paged requests against one series must not each
    // re-read the roster and every owned key. After the first request fills the cache the owned
    // keys and the roster are never read again, and every page slices the same stable lists.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_ReconcilesOnceForManyPageLoads()
    {
        var roster = Enumerable.Range(1, 55)
            .Select(i => MakeExpected(i, $"Expected {i:00}", (i % 7).ToString()))
            .ToList();
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = roster,
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>(), catalogRow);

        var service = MakeService();
        for (var skip = 0; skip < 55; skip += 10)
        {
            var page = await service.GetSeriesDetailPageAsync("Mistborn", 0, 100, skip, 10, 0, 10, 0, 10);
            Assert.AreEqual(Math.Min(10, 55 - skip), page!.MissingBooks.Count);
            Assert.AreEqual(55, page.MissingBookTotal, "the total is the full reconciled roster each time, not the slice");
        }

        _audiobookRepository.Verify(r => r.GetSeriesOwnedKeysAsync("Mistborn", It.IsAny<int>()), Times.Once,
            "the reconciliation must be computed once, not once per page request");
        _seriesRepository.Verify(r => r.GetByNameWithExpectedBooksBoundedAsync("Mistborn", It.IsAny<int>()), Times.Once,
            "the roster must be read once, not once per page request");
    }

    // The cache-contract wiring, not just the cache itself: flipping an ignore flag through the
    // service must invalidate the reconciled detail so the next page loads the fresh state.
    [TestMethod]
    public async Task IgnoreExpectedBookAsync_InvalidatesTheCachedReconciliation()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(1, "Secret History", "3.5") },
        };
        StubSeries("Mistborn", new List<SeriesGroupingBook>(), catalogRow);
        _seriesRepository
            .Setup(r => r.SetExpectedBookIgnoredAsync("Mistborn", "3.5", "Secret History", true))
            .Callback(() => catalogRow.ExpectedBooks.Single().IsIgnored = true)
            .Returns(Task.CompletedTask);

        var service = MakeService();
        var before = await service.GetSeriesDetailPageAsync("Mistborn", 0, 100, 0, 100, 0, 100, 0, 100);
        Assert.IsNotNull(before);
        Assert.AreEqual(1, before.MissingBookTotal);

        await service.IgnoreExpectedBookAsync("Mistborn", "3.5", "Secret History", true);

        var after = await service.GetSeriesDetailPageAsync("Mistborn", 0, 100, 0, 100, 0, 100, 0, 100);
        Assert.IsNotNull(after);
        Assert.AreEqual(0, after.MissingBookTotal, "the ignored entry must leave the missing section");
        Assert.AreEqual(1, after.IgnoredBookTotal, "the ignored entry must appear under ignored");
    }

    // Same wiring proof for the omnibus toggle: flipping visibility through the service must
    // invalidate the cache, so the newly-visible compilations are reported as missing on the next
    // load rather than echoing the pre-toggle reconciliation.
    [TestMethod]
    public async Task SetIncludeOmnibusEditionsAsync_RefillsTheCachedReconciliation()
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
        StubSeries("Thursday Murder Club", new List<SeriesGroupingBook>(), catalogRow);
        _seriesRepository
            .Setup(r => r.SetIncludeOmnibusEditionsAsync("Thursday Murder Club", true))
            .Callback(() => catalogRow.IncludeOmnibusEditions = true)
            .ReturnsAsync(catalogRow);

        var service = MakeService();
        var before = await service.GetSeriesDetailPageAsync("Thursday Murder Club", 0, 100, 0, 100, 0, 100, 0, 100);
        Assert.IsNotNull(before);
        Assert.AreEqual(1, before.MissingBookTotal);

        await service.SetIncludeOmnibusEditionsAsync("Thursday Murder Club", true);

        var after = await service.GetSeriesDetailPageAsync("Thursday Murder Club", 0, 100, 0, 100, 0, 100, 0, 100);
        Assert.IsNotNull(after);
        Assert.AreEqual(2, after.MissingBookTotal, "the toggle must invalidate the cache so the detail refills");
    }

    // Regression for the materialization caps: the bounded roster read reports an oversized
    // roster, and the reconciliation must fail clearly without ever fetching the owned keys (the
    // second potentially-pathological input) - proving the cap is enforced before any further
    // unbounded materialization can happen.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_RosterOverTheCap_FailsBeforeReadingOwnedKeys()
    {
        _seriesRepository.Setup(r => r.GetByNameAsync("Mistborn")).ReturnsAsync(
            new Series { Id = 1, Name = "Mistborn", MatchedSourceName = "Hardcover", MatchedSourceId = "42" });
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedBooksPageAsync("Mistborn", 0, 100))
            .ReturnsAsync((new List<SeriesOwnedBookRow>(), 1));
        _seriesRepository
            .Setup(r => r.GetByNameWithExpectedBooksBoundedAsync("Mistborn", SeriesService.MaxReconciliationRosterEntries))
            .ReturnsAsync((new Series { Id = 1, Name = "Mistborn" }, Overflow: true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => GetDetailPageAsync("Mistborn"));

        _seriesRepository.Verify(
            r => r.GetByNameWithExpectedBooksBoundedAsync("Mistborn", SeriesService.MaxReconciliationRosterEntries),
            Times.Once,
            "the roster fetch is bounded to the reconciliation cap");
        _audiobookRepository.Verify(r => r.GetSeriesOwnedKeysAsync("Mistborn", It.IsAny<int>()), Times.Never,
            "roster overflow must fail the request before the owned set is fetched");
    }

    // Same for the owned side: a normal roster plus an owned-key overflow fails clearly, and the
    // owned fetch reaches the repository bounded to the owned cap.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_OwnedKeysOverTheCap_FailsClearly()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>(),
        };
        _seriesRepository.Setup(r => r.GetByNameAsync("Mistborn")).ReturnsAsync(catalogRow);
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedBooksPageAsync("Mistborn", 0, 100))
            .ReturnsAsync((new List<SeriesOwnedBookRow>(), 1));
        _seriesRepository
            .Setup(r => r.GetByNameWithExpectedBooksBoundedAsync("Mistborn", SeriesService.MaxReconciliationRosterEntries))
            .ReturnsAsync((catalogRow, Overflow: false));
        _audiobookRepository
            .Setup(r => r.GetSeriesOwnedKeysAsync("Mistborn", SeriesService.MaxReconciliationOwnedKeys))
            .ReturnsAsync((new List<SeriesOwnedKey>(), Overflow: true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => GetDetailPageAsync("Mistborn"));

        _audiobookRepository.Verify(
            r => r.GetSeriesOwnedKeysAsync("Mistborn", SeriesService.MaxReconciliationOwnedKeys),
            Times.Once,
            "the owned-key fetch is bounded to the reconciliation cap");
    }

    #region Part mismatch reconciliation

    // A roster entry with no position defines nothing to fix a stored part against. A book that
    // hand-carries a part under such an entry must not be flagged (resolving would clear a value
    // on evidence that "differs from a position" does not describe), and its ExpectedPart could not
    // be filled in.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_StoredPartUnderAPositionlessRosterEntry_IsNotReported()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(1, "The Final Empire", null) },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "1", "The Final Empire"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.PartMismatchTotal, "no roster position means nothing to mismatch against");
        Assert.AreEqual(0, page.MissingBookTotal, "the title still identifies the book as owned");
    }

    // A book whose stored part differs from the position its roster entry assigns is not missing
    // (the book is there) - it is a part mismatch, reported with the book identity, both parts and
    // the roster title the fix writes back through.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_OwnedBookWithADifferentPart_IsReportedAsAPartMismatch()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(1, "The Final Empire", "1") },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "7", "The Final Empire"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.MissingBookTotal, "the wrong-part book is owned, not missing");
        Assert.AreEqual(1, page.PartMismatchTotal);
        var mismatch = page.PartMismatches.Single();
        Assert.AreEqual(1, mismatch.AudiobookId);
        Assert.AreEqual("The Final Empire", mismatch.BookName);
        Assert.AreEqual("7", mismatch.StoredPart);
        Assert.AreEqual("1", mismatch.ExpectedPart);
        Assert.AreEqual("The Final Empire", mismatch.RosterTitle);
    }

    // A book with no part at all opposite a roster entry that assigns one is the "missing part"
    // shape of this feature - the book was matched on title, so it is not missing, but its part
    // gap is exactly what the section exists to fix.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_OwnedBookWithNoPart_IsReportedAsAPartMismatch()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(1, "The Well of Ascension", "2") },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", null, "The Well of Ascension"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.PartMismatchTotal);
        var mismatch = page.PartMismatches.Single();
        Assert.AreEqual(1, mismatch.AudiobookId);
        Assert.IsNull(mismatch.StoredPart);
        Assert.AreEqual("2", mismatch.ExpectedPart);
    }

    // The part comparison reuses the matching equivalence: "2" and "2.0" are the same part, so a
    // differently-typed-but-equivalent stored part must not be flagged.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_EquivalentStoredPart_IsNotReportedAsAMismatch()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook> { MakeExpected(1, "The Final Empire", "2") },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "2.0", "The Final Empire"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.PartMismatchTotal, "2.0 and 2 are the same part");
        Assert.AreEqual(0, page.MissingBookTotal);
    }

    // A truly unowned roster entry is missing; it must not double-report as a part mismatch (there
    // is no owned book whose part could disagree with it).
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_UnownedRosterEntry_IsMissingNotAPartMismatch()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(1, "The Final Empire", "1"),
                MakeExpected(2, "The Hero of Ages", "3"),
            },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "1", "The Final Empire"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.MissingBookTotal);
        Assert.AreEqual("The Hero of Ages", page.MissingBooks.Single().Title);
        Assert.AreEqual(0, page.PartMismatchTotal);
    }

    // Ignored roster entries are excluded from the missing section; their books' parts must not be
    // reported either - a book can only be "mismatched" against a roster entry the user has told
    // us to stop tracking.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_IgnoredRosterEntry_ProducesNoPartMismatch()
    {
        var catalogRow = new Series
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

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "9", "Secret History"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.PartMismatchTotal, "the book matches only an ignored entry");
        Assert.AreEqual(0, page.MissingBookTotal);
        Assert.AreEqual(1, page.IgnoredBookTotal);
    }

    // A compilation entry that is hidden by the omnibus setting must not surface its book's part,
    // exactly like it cannot surface as missing: the roster entry is not part of the visible series.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_HiddenCompilation_ProducesNoPartMismatch()
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
                new()
                {
                    Id = 2,
                    Title = "The Thursday Murder Club / The Man Who Died Twice",
                    Position = "1",
                    IsCompilation = true,
                },
            },
        };

        StubSeries("Thursday Murder Club", new List<SeriesGroupingBook>
        {
            MakeGrouping("Thursday Murder Club", "3", "The Thursday Murder Club / The Man Who Died Twice"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Thursday Murder Club");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.PartMismatchTotal, "the hidden compilation is not part of the visible series");

        // Flipping the visibility setting makes the same book a plain mismatch, proving the
        // visibility rule (not the ownership) is what kept it out before.
        catalogRow.IncludeOmnibusEditions = true;
        _reconciliationCache.Invalidate("Thursday Murder Club");
        var pageWithOmnibus = await GetDetailPageAsync("Thursday Murder Club");
        Assert.IsNotNull(pageWithOmnibus);
        Assert.AreEqual(1, pageWithOmnibus.PartMismatchTotal);
    }

    // The part-mismatch section is paged from the cached reconciliation like the missing/ignored
    // sections: one mismatch per book, stable slice boundaries.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_PagesThePartMismatchSection_NoBookSkippedOrRepeated()
    {
        var grouping = Enumerable.Range(1, 23)
            .Select(i => MakeGrouping("Mistborn", (99 + i).ToString(), $"Book {i:00}"))
            .ToList();
        var roster = Enumerable.Range(1, 23)
            .Select(i => MakeExpected(i, $"Book {i:00}", i.ToString()))
            .ToList();
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = roster,
        };

        StubSeries("Mistborn", grouping, catalogRow);

        var seen = new List<long>();
        for (var skip = 0; skip < 23; skip += 10)
        {
            var page = await GetDetailPageAsync("Mistborn", partMismatchSkip: skip, partMismatchTake: 10);
            seen.AddRange(page!.PartMismatches.Select(m => m.AudiobookId));
        }

        Assert.AreEqual(23, seen.Count, "no mismatch may be dropped by paging");
        Assert.AreEqual(23, seen.Distinct().Count(), "no mismatch may appear on two pages");
    }

    // Regression: attribution used to be per roster entry with first-wins handling - the first
    // entry in display order that matched a book claimed it. With a title repeated at two
    // positions (say a source listing the individual volume and an omnibus reissue of the same
    // book), the book's stored part agreed with the SECOND entry, but the FIRST entry (an
    // earlier position) also matched by title alone, so the book was flagged as a part mismatch
    // with the wrong expected part. A stored part that agrees with ANY matched entry is correct
    // - a duplicate-title sibling's disagreement is not evidence the book is misnumbered.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_DuplicateTitles_StoredPartAgreeingWithOneEntry_IsNotFlagged()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(1, "The Final Empire", "1"),
                MakeExpected(2, "The Final Empire", "2"),
            },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "2", "The Final Empire"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(0, page.PartMismatchTotal,
            "a stored part agreeing with one of the duplicate-title entries is not a mismatch");
        Assert.AreEqual(0, page.MissingBookTotal, "the title still identifies the book as owned");
    }

    // The same duplicate-title roster, but the book's stored part agrees with neither entry -
    // that is a genuine mismatch, still reported exactly once and attributed to the
    // earliest-position entry the book matched.
    [TestMethod]
    public async Task GetSeriesDetailPageAsync_DuplicateTitles_StoredPartAgreeingWithNoEntry_IsFlaggedOnce()
    {
        var catalogRow = new Series
        {
            Id = 1,
            Name = "Mistborn",
            MatchedSourceName = "Hardcover",
            MatchedSourceId = "42",
            ExpectedBooks = new List<SeriesExpectedBook>
            {
                MakeExpected(1, "The Final Empire", "1"),
                MakeExpected(2, "The Final Empire", "2"),
            },
        };

        StubSeries("Mistborn", new List<SeriesGroupingBook>
        {
            MakeGrouping("Mistborn", "9", "The Final Empire"),
        }, catalogRow);

        var page = await GetDetailPageAsync("Mistborn");

        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.PartMismatchTotal);
        var mismatch = page.PartMismatches.Single();
        Assert.AreEqual(1, mismatch.AudiobookId);
        Assert.AreEqual("9", mismatch.StoredPart);
        Assert.AreEqual("1", mismatch.ExpectedPart, "the earliest matched entry is the fix target");
    }

    #endregion

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

    // The author detail's series section pages the same overview pipeline scoped to one author:
    // the authorId must reach the repository so the page and its total are that author's series
    // values only, while the page's values are still hydrated into full overviews.
    [TestMethod]
    public async Task GetSeriesOverviewPageAsync_PassesAuthorIdToTheRepository()
    {
        _audiobookRepository
            .Setup(r => r.GetSeriesValuesPageAsync(null, null, 0, 50, 7))
            .ReturnsAsync((new List<string> { "Mistborn" }, 2));
        _audiobookRepository
            .Setup(r => r.GetSeriesGroupingDataAsync(new List<string> { "Mistborn" }))
            .ReturnsAsync(new List<SeriesGroupingBook>
            {
                new("Mistborn", "1", "The Final Empire", new List<string> { "Brandon Sanderson" }),
                new("Mistborn", "2", "The Well of Ascension", new List<string> { "Brandon Sanderson" }),
            });
        _seriesRepository
            .Setup(r => r.GetByNamesWithExpectedBooksAsync(new List<string> { "Mistborn" }))
            .ReturnsAsync(new List<Series>());

        var page = await MakeService().GetSeriesOverviewPageAsync(0, 50, null, null, authorId: 7);

        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Mistborn", page.Items[0].Name);
        Assert.AreEqual(2, page.Items[0].OwnedBookCount);
        Assert.IsFalse(page.Items[0].IsMatched);
        CollectionAssert.AreEqual(new List<string> { "Brandon Sanderson" }, page.Items[0].Authors);
        _audiobookRepository.Verify(r => r.GetSeriesValuesPageAsync(null, null, 0, 50, 7), Times.Once);
    }

    // The overview must stay library-wide in its owned/missing figures even when the page of
    // series values is author-scoped: a series entry lists what the whole library owns, so the
    // catalog data used to build it is not author-filtered. Confirmed by a matched catalog row
    // whose roster reports missing books.
    [TestMethod]
    public async Task GetSeriesOverviewPageAsync_AuthorScopedPage_StillBuildsWholeLibraryOverview()
    {
        _audiobookRepository
            .Setup(r => r.GetSeriesValuesPageAsync(null, null, 0, 50, 7))
            .ReturnsAsync((new List<string> { "Mistborn" }, 1));
        _audiobookRepository
            .Setup(r => r.GetSeriesGroupingDataAsync(new List<string> { "Mistborn" }))
            .ReturnsAsync(new List<SeriesGroupingBook>
            {
                new("Mistborn", "1", "The Final Empire", new List<string> { "Brandon Sanderson" }),
            });
        _seriesRepository
            .Setup(r => r.GetByNamesWithExpectedBooksAsync(new List<string> { "Mistborn" }))
            .ReturnsAsync(new List<Series>
            {
                new()
                {
                    Id = 1,
                    Name = "Mistborn",
                    MatchedSourceName = "Hardcover",
                    MatchedSourceId = "42",
                    ExpectedBooks = new List<SeriesExpectedBook>
                    {
                        MakeExpected(10, "The Final Empire", "1"),
                        MakeExpected(11, "The Hero of Ages", "3"),
                    },
                },
            });

        var page = await MakeService().GetSeriesOverviewPageAsync(0, 50, null, null, authorId: 7);

        Assert.AreEqual(1, page.Items.Count);
        var overview = page.Items[0];
        Assert.IsTrue(overview.IsMatched);
        Assert.AreEqual("Hardcover", overview.MatchedSourceName);
        Assert.AreEqual(1, overview.MissingBookCount,
            "missing counts describe the whole library's holdings, matching the /library/series page");
        Assert.AreEqual(2, overview.ExpectedBookCount);
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
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksBoundedAsync("Mistborn", It.IsAny<int>()))
            .ReturnsAsync((existing, Overflow: false));
        _audiobookRepository.Setup(r => r.GetSeriesOwnedKeysAsync("Mistborn", It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), Overflow: false));
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Mistborn"))
            .ReturnsAsync(new List<string>());

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
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksBoundedAsync("Thursday Murder Club", It.IsAny<int>()))
            .ReturnsAsync(((Series?)null, Overflow: false));
        _audiobookRepository.Setup(r => r.GetSeriesOwnedKeysAsync("Thursday Murder Club", It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), Overflow: false));
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Thursday Murder Club"))
            .ReturnsAsync(new List<string>());

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
    public async Task GetSeriesDetailPageAsync_HidesCompilationsUnlessIncludeOmnibusEditionsIsSet()
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

        StubSeries("Thursday Murder Club", new List<SeriesGroupingBook>(), catalogRow);

        var page = await GetDetailPageAsync("Thursday Murder Club");

        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.MissingBooks.Count);
        Assert.AreEqual("The Thursday Murder Club", page.MissingBooks.Single().Title);
        Assert.AreEqual(1, page.Overview.MissingBookCount);

        // Flipping the visibility setting goes through SetIncludeOmnibusEditionsAsync, which
        // invalidates the cached reconciliation; mirror that here so the second page load starts
        // from a fresh read (the cache-contract test covers the invalidate call itself).
        catalogRow.IncludeOmnibusEditions = true;
        _reconciliationCache.Invalidate("Thursday Murder Club");

        var pageWithOmnibus = await GetDetailPageAsync("Thursday Murder Club");

        Assert.IsNotNull(pageWithOmnibus);
        Assert.AreEqual(2, pageWithOmnibus.MissingBooks.Count);
        Assert.AreEqual(2, pageWithOmnibus.Overview.MissingBookCount);
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
        _seriesRepository.Setup(r => r.GetByNameWithExpectedBooksBoundedAsync("Thursday Murder Club", It.IsAny<int>()))
            .ReturnsAsync((existing, Overflow: false));
        _audiobookRepository.Setup(r => r.GetSeriesOwnedKeysAsync("Thursday Murder Club", It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), Overflow: false));
        _audiobookRepository.Setup(r => r.GetAuthorNamesBySeriesAsync("Thursday Murder Club"))
            .ReturnsAsync(new List<string>());

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
