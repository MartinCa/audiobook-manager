using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using SeriesOverview = AudiobookManager.Domain.SeriesOverview;
using SeriesOverviewPage = AudiobookManager.Domain.SeriesOverviewPage;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class BrowseControllerTests
{
    private Mock<IAudiobookRepository> _audiobookRepo = null!;
    private Mock<IPersonRepository> _personRepo = null!;
    private Mock<IGenreRepository> _genreRepo = null!;
    private Mock<ISeriesService> _seriesService = null!;
    private Mock<IUpcomingReleaseService> _upcomingReleaseService = null!;
    private Mock<IAuthorReconciliationProvider> _authorReconciliation = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private ExpectedBookWriteGate _expectedBookWriteGate = null!;
    private BrowseController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepo = new Mock<IAudiobookRepository>();
        _personRepo = new Mock<IPersonRepository>();
        _genreRepo = new Mock<IGenreRepository>();
        _seriesService = new Mock<ISeriesService>();
        _upcomingReleaseService = new Mock<IUpcomingReleaseService>();
        _authorReconciliation = new Mock<IAuthorReconciliationProvider>();
        _authorReconciliation.Setup(r => r.GetReconciliationAsync(It.IsAny<long>(), It.IsAny<bool>()))
            .ReturnsAsync(new AuthorReconciliation(
                new List<AuthorExpectedBookInfo>(), new List<AuthorExpectedBookInfo>(), new List<AuthorExpectedBookInfo>(),
                0, 0, new List<AuthorMissingSeriesInfo>()));
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _expectedBookWriteGate = new ExpectedBookWriteGate();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IUpcomingReleaseService))).Returns(_upcomingReleaseService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _controller = new BrowseController(
            _audiobookRepo.Object, _personRepo.Object, _genreRepo.Object, _seriesService.Object,
            _upcomingReleaseService.Object, _authorReconciliation.Object, _expectedBookWriteGate,
            Array.Empty<IScraper>(),
            _serviceScopeFactory.Object, _statusRegistry.Object, Mock.Of<IHostApplicationLifetime>(),
            Mock.Of<ILogger<BrowseController>>());
    }

    // BackgroundOperationRunner calls statusRegistry.SetFinished(key) and THEN releases the
    // gate in its finally block - see SeriesControllerTests for the full rationale.
    private Task RegisterFinishedWaiter(string operationKey)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry.Setup(r => r.SetFinished(operationKey)).Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private static async Task AwaitOperationFinished(Task finishedSignal, ExpectedBookWriteGate gate)
    {
        await finishedSignal.WaitAsync(TimeSpan.FromSeconds(5));
        await OperationGate.WaitUntilReleasedAsync(gate);
    }

    private static Audiobook MakeBook(long id, string bookName, string? series = null) =>
        new(id, bookName, null, series, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);

    [TestMethod]
    public async Task SearchLibrary_BlankQuery_ReturnsEmptyResult()
    {
        var result = await _controller.SearchLibrary("   ");

        Assert.AreEqual(0, result.Books.Count);
        Assert.AreEqual(0, result.Authors.Count);
        Assert.AreEqual(0, result.Series.Count);
        _audiobookRepo.Verify(
            r => r.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SearchLibrary_CombinesBooksAuthorsAndSeries()
    {
        var book = MakeBook(1, "Mistborn: The Final Empire", "Mistborn");
        book.Authors = new List<Person> { new(1, "Brandon Sanderson") };

        _audiobookRepo.Setup(r => r.SearchAsync("mist", 5, 0, false, false))
            .ReturnsAsync((new List<Audiobook> { book }, 1));
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("mist", 5, 0))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("mist", 5, 0))
            .ReturnsAsync((new List<(string Series, int BookCount)> { ("Mistborn", 3) }, 1));

        var result = await _controller.SearchLibrary("mist");

        Assert.AreEqual(1, result.Books.Count);
        Assert.AreEqual("Mistborn: The Final Empire", result.Books[0].BookName);
        CollectionAssert.Contains(result.Books[0].Authors, "Brandon Sanderson");
        Assert.AreEqual(1, result.Series.Count);
        Assert.AreEqual("Mistborn", result.Series[0].Name);
        Assert.AreEqual(3, result.Series[0].BookCount);
        Assert.AreEqual(0, result.Authors.Count);
    }

    // Ranking moved into the repositories, which order prefix matches first *before* their
    // LIMIT - the controller must now pass that order through untouched. Re-ranking here was
    // both redundant and wrong: it compared with a plain OrdinalIgnoreCase StartsWith, which is
    // not accent-insensitive, so it demoted a "René" row that had correctly prefix-matched a
    // "Rene" query. (The truncation bug this used to paper over is covered by
    // AudiobookRepositorySearchTests/PersonRepositorySearchTests.)
    [TestMethod]
    public async Task SearchLibrary_PreservesTheOrderTheRepositoryRankedIn()
    {
        var prefixMatch = new AuthorSummaryRow(1, "San Diego", 1);
        var substringMatch = new AuthorSummaryRow(2, "Brandon Sanderson", 1);

        _audiobookRepo.Setup(r => r.SearchAsync("san", 5, 0, false, false)).ReturnsAsync((new List<Audiobook>(), 0));
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("san", 5, 0)).ReturnsAsync((new List<(string Series, int BookCount)>(), 0));
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("san", 5, 0))
            .ReturnsAsync((new List<AuthorSummaryRow> { prefixMatch, substringMatch }, 2));

        var result = await _controller.SearchLibrary("san");

        Assert.AreEqual(2, result.Authors.Count);
        Assert.AreEqual("San Diego", result.Authors[0].Name);
        Assert.AreEqual("Brandon Sanderson", result.Authors[1].Name);
    }

    // Regression test for the accent case the removed client-side ranking got wrong: an
    // accent-folded prefix hit ranked first by the repository must not be reordered here.
    [TestMethod]
    public async Task SearchLibrary_DoesNotDemoteAnAccentFoldedPrefixMatch()
    {
        var accentedPrefixMatch = new AuthorSummaryRow(1, "Réne Girard", 1);
        var substringMatch = new AuthorSummaryRow(2, "Marie Irene", 1);

        _audiobookRepo.Setup(r => r.SearchAsync("rene", 5, 0, false, false)).ReturnsAsync((new List<Audiobook>(), 0));
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("rene", 5, 0)).ReturnsAsync((new List<(string Series, int BookCount)>(), 0));
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("rene", 5, 0))
            .ReturnsAsync((new List<AuthorSummaryRow> { accentedPrefixMatch, substringMatch }, 2));

        var result = await _controller.SearchLibrary("rene");

        Assert.AreEqual(2, result.Authors.Count);
        Assert.AreEqual("Réne Girard", result.Authors[0].Name);
        Assert.AreEqual("Marie Irene", result.Authors[1].Name);
    }

    [TestMethod]
    public async Task SearchAuthors_BlankQuery_ReturnsEmptyPageAndNeverTouchesTheRepository()
    {
        var result = await _controller.SearchAuthors("   ");

        Assert.AreEqual(0, result.Value!.Count);
        Assert.AreEqual(0, result.Value!.Total);
        _personRepo.Verify(
            r => r.SearchAuthorSummariesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SearchAuthors_MapsRowsToDtosAndPassesLimitAndOffsetThrough()
    {
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("sand", 20, 40))
            .ReturnsAsync((new List<AuthorSummaryRow> { new(1, "Brandon Sanderson", 5) }, 37));

        var result = await _controller.SearchAuthors("sand", 20, 40);

        Assert.AreEqual(1, result.Value!.Count);
        Assert.AreEqual(37, result.Value!.Total);
        Assert.AreEqual("Brandon Sanderson", result.Value!.Items[0].Name);
        Assert.AreEqual(5, result.Value!.Items[0].BookCount);
        _personRepo.Verify(r => r.SearchAuthorSummariesAsync("sand", 20, 40), Times.Once);
    }

    [TestMethod]
    public async Task SearchAuthors_LimitBelowOne_ReturnsBadRequestAndNeverTouchesTheRepository()
    {
        var result = await _controller.SearchAuthors("sand", 0, 0);

        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(400, objectResult!.StatusCode);
        _personRepo.Verify(
            r => r.SearchAuthorSummariesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SearchAuthors_LimitAboveMax_ReturnsBadRequest()
    {
        var result = await _controller.SearchAuthors("sand", 101, 0);

        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(400, objectResult!.StatusCode);
    }

    [TestMethod]
    public async Task SearchAuthors_NegativeOffset_ReturnsBadRequest()
    {
        var result = await _controller.SearchAuthors("sand", 20, -1);

        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(400, objectResult!.StatusCode);
    }

    [TestMethod]
    public async Task SearchAuthors_OffsetAboveMax_ReturnsBadRequest()
    {
        var result = await _controller.SearchAuthors("sand", 20, 1_000_001);

        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(400, objectResult!.StatusCode);
    }

    [TestMethod]
    public async Task SearchSeries_BlankQuery_ReturnsEmptyPageAndNeverTouchesTheRepository()
    {
        var result = await _controller.SearchSeries("   ");

        Assert.AreEqual(0, result.Value!.Count);
        Assert.AreEqual(0, result.Value!.Total);
        _audiobookRepo.Verify(
            r => r.SearchSeriesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SearchSeries_MapsRowsToDtosAndPassesLimitAndOffsetThrough()
    {
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("mist", 20, 40))
            .ReturnsAsync((new List<(string Series, int BookCount)> { ("Mistborn", 3) }, 22));

        var result = await _controller.SearchSeries("mist", 20, 40);

        Assert.AreEqual(1, result.Value!.Count);
        Assert.AreEqual(22, result.Value!.Total);
        Assert.AreEqual("Mistborn", result.Value!.Items[0].Name);
        Assert.AreEqual(3, result.Value!.Items[0].BookCount);
        _audiobookRepo.Verify(r => r.SearchSeriesAsync("mist", 20, 40), Times.Once);
    }

    [TestMethod]
    public async Task SearchSeries_LimitAboveMax_ReturnsBadRequestAndNeverTouchesTheRepository()
    {
        var result = await _controller.SearchSeries("mist", 101, 0);

        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(400, objectResult!.StatusCode);
        _audiobookRepo.Verify(
            r => r.SearchSeriesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SearchSeries_NegativeOffset_ReturnsBadRequest()
    {
        var result = await _controller.SearchSeries("mist", 20, -1);

        var objectResult = result.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(400, objectResult!.StatusCode);
    }

    [TestMethod]
    public async Task GetAuthors_ReturnsOnePageWithItemsAndTotal()
    {
        _personRepo.Setup(r => r.GetAuthorSummariesPagedAsync(null, 50, 0))
            .ReturnsAsync((new List<AuthorSummaryRow> { new(1, "Brandon Sanderson", 5) }, 91));

        var result = await _controller.GetAuthors();

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        Assert.AreEqual(1, ok.Count);
        Assert.AreEqual(91, ok.Total);
        Assert.AreEqual("Brandon Sanderson", ok.Items[0].Name);
        Assert.AreEqual(5, ok.Items[0].BookCount);
    }

    [TestMethod]
    public async Task GetAuthors_PassesSearchLimitAndOffsetThrough()
    {
        _personRepo.Setup(r => r.GetAuthorSummariesPagedAsync("sand", 25, 50))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));

        var result = await _controller.GetAuthors(q: "sand", limit: 25, offset: 50);

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        _personRepo.Verify(r => r.GetAuthorSummariesPagedAsync("sand", 25, 50), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthors_BlankSearchBecomesNoFilter()
    {
        _personRepo.Setup(r => r.GetAuthorSummariesPagedAsync(null, 20, 0))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));

        var result = await _controller.GetAuthors(q: "   ", limit: 20);

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        _personRepo.Verify(r => r.GetAuthorSummariesPagedAsync(null, 20, 0), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthors_PassesNewFiltersThrough()
    {
        var expectedFilter = new AuthorSummaryFilter(
            Followed: true, MinBookCount: 2, MaxBookCount: null, HasMissingBooks: null,
            HasUpcomingBooks: null, Matched: true, RefreshedAfter: null, RefreshedBefore: null, NeverRefreshed: null);

        _personRepo
            .Setup(r => r.GetAuthorSummariesPagedAsync(
                null, 50, 0, expectedFilter, null, null))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));

        var result = await _controller.GetAuthors(followed: true, minBookCount: 2, matched: true);

        Assert.IsNotNull(result.Value);
        _personRepo.Verify(
            r => r.GetAuthorSummariesPagedAsync(null, 50, 0, expectedFilter, null, null), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthors_HasMissingBooksFilter_RestrictsToTheReconciledIdSet()
    {
        _authorReconciliation
            .Setup(r => r.GetBulkMissingOrUpcomingAuthorIdsAsync())
            .ReturnsAsync(new AuthorBulkReconciliationResult(
                new HashSet<long> { 1, 2 }, new HashSet<long> { 2, 3 }, Refused: false));

        _personRepo
            .Setup(r => r.GetAuthorSummariesPagedAsync(
                null, 50, 0, It.IsAny<AuthorSummaryFilter>(),
                It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 })), null))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));

        var result = await _controller.GetAuthors(hasMissingBooks: true);

        Assert.IsNotNull(result.Value);
        _personRepo.Verify(
            r => r.GetAuthorSummariesPagedAsync(
                null, 50, 0, It.IsAny<AuthorSummaryFilter>(),
                It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 })), null),
            Times.Once);
    }

    [TestMethod]
    public async Task GetAuthors_HasMissingBooksFalse_ExcludesTheReconciledIdSet()
    {
        _authorReconciliation
            .Setup(r => r.GetBulkMissingOrUpcomingAuthorIdsAsync())
            .ReturnsAsync(new AuthorBulkReconciliationResult(
                new HashSet<long> { 1, 2 }, new HashSet<long>(), Refused: false));

        _personRepo
            .Setup(r => r.GetAuthorSummariesPagedAsync(
                null, 50, 0, It.IsAny<AuthorSummaryFilter>(), null,
                It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 }))))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));

        var result = await _controller.GetAuthors(hasMissingBooks: false);

        Assert.IsNotNull(result.Value);
        _personRepo.Verify(
            r => r.GetAuthorSummariesPagedAsync(
                null, 50, 0, It.IsAny<AuthorSummaryFilter>(), null,
                It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2 }))),
            Times.Once);
    }

    // The bulk classifier refuses (reports Refused) rather than truncating when the unified
    // roster exceeds its bounded read; the list keeps every other filter and simply skips the
    // missing/upcoming one instead of applying it to a wrong (truncated) set.
    [TestMethod]
    public async Task GetAuthors_ReconciliationRefused_AppliesTheRemainingFiltersWithoutTheMissingUpcomingOne()
    {
        _authorReconciliation
            .Setup(r => r.GetBulkMissingOrUpcomingAuthorIdsAsync())
            .ReturnsAsync(new AuthorBulkReconciliationResult(
                new HashSet<long>(), new HashSet<long>(), Refused: true));

        _personRepo
            .Setup(r => r.GetAuthorSummariesPagedAsync(null, 50, 0, It.IsAny<AuthorSummaryFilter>(), null, null))
            .ReturnsAsync((new List<AuthorSummaryRow>(), 0));

        var result = await _controller.GetAuthors(hasMissingBooks: true, followed: true);

        Assert.IsNotNull(result.Value);
        _personRepo.Verify(
            r => r.GetAuthorSummariesPagedAsync(null, 50, 0, It.IsAny<AuthorSummaryFilter>(), null, null), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthors_MinBookCountGreaterThanMax_IsRefused()
    {
        var result = await _controller.GetAuthors(minBookCount: 5, maxBookCount: 1);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
    }

    [TestMethod]
    public async Task GetAuthors_AnOutOfRangeOffset_IsRefusedWithoutTouchingTheRepository()
    {
        var result = await _controller.GetAuthors(limit: 50, offset: 1_000_001);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
        _personRepo.Verify(
            r => r.GetAuthorSummariesPagedAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetAuthorDetail_ReturnsPagedSectionsWithTheirTotals()
    {
        var authorRow = new AuthorSummaryRow(7, "Brandon Sanderson", 5);
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(authorRow);
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage
            {
                Items = new List<SeriesOverview>
                {
                    new()
                    {
                        Name = "Mistborn",
                        OwnedBookCount = 3,
                    },
                },
                TotalCount = 2,
            });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 4));

        var result = await _controller.GetAuthorDetail(7);

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        Assert.AreEqual(7, ok.Author.Id);
        Assert.AreEqual(1, ok.Series.Count);
        Assert.AreEqual(2, ok.Series.Total);
        Assert.AreEqual("Mistborn", ok.Series.Items[0].Name);
        Assert.AreEqual(3, ok.Series.Items[0].OwnedBookCount);
        Assert.AreEqual(0, ok.StandaloneBooks.Count);
        Assert.AreEqual(4, ok.StandaloneBooks.Total);
    }

    // The author detail's series section now runs through the same overview pipeline as the
    // /library/series page, so each entry must carry the match state, authors and owned/missing
    // counts the rich renderer shows - not just a name and a count.
    [TestMethod]
    public async Task GetAuthorDetail_SeriesSectionCarriesMatchAndMissingData()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage
            {
                Items = new List<SeriesOverview>
                {
                    new()
                    {
                        Id = 1,
                        Name = "Mistborn",
                        Authors = new List<string> { "Brandon Sanderson" },
                        OwnedBookCount = 3,
                        IsMatched = true,
                        MatchedSourceName = "Hardcover",
                        MatchedSourceId = "42",
                        MatchConfidence = 0.75,
                        ExpectedBookCount = 4,
                        MissingBookCount = 1,
                        IgnoredBookCount = 0,
                    },
                    new()
                    {
                        Name = "Skyward",
                        OwnedBookCount = 2,
                        IsMatched = false,
                    },
                },
                TotalCount = 2,
            });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));

        var result = await _controller.GetAuthorDetail(7);

        var ok = result.Value!;
        var matched = ok.Series.Items[0];
        Assert.AreEqual("Mistborn", matched.Name);
        Assert.AreEqual(3, matched.OwnedBookCount);
        CollectionAssert.Contains(matched.Authors, "Brandon Sanderson");
        Assert.IsTrue(matched.IsMatched);
        Assert.AreEqual("Hardcover", matched.MatchedSourceName);
        Assert.AreEqual(0.75, matched.MatchConfidence);
        Assert.AreEqual(1, matched.MissingBookCount);
        Assert.AreEqual(4, matched.ExpectedBookCount);
        var unmatched = ok.Series.Items[1];
        Assert.AreEqual("Skyward", unmatched.Name);
        Assert.IsFalse(unmatched.IsMatched);
        Assert.AreEqual(0, unmatched.MissingBookCount);
        _seriesService.Verify(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7), Times.Once,
            "the series page must be scoped to the author's own series values");
    }

    [TestMethod]
    public async Task GetAuthorDetail_PassesSectionPagingThrough()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(2, 25, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 10, 20))
            .ReturnsAsync((new List<Audiobook>(), 0));

        var result = await _controller.GetAuthorDetail(
            authorId: 7, seriesLimit: 25, seriesOffset: 50, standaloneLimit: 10, standaloneOffset: 20);

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        _seriesService.Verify(s => s.GetSeriesOverviewPageAsync(2, 25, null, null, 7), Times.Once,
            "seriesLimit/seriesOffset map to the service's page/pageSize (offset / limit)");
        _audiobookRepo.Verify(r => r.GetStandaloneBooksByAuthorAsync(7, 10, 20), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthorDetail_AnInvalidSectionLimit_IsRefusedWithoutCallingTheRepository()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));

        var result = await _controller.GetAuthorDetail(authorId: 7, standaloneLimit: 0);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(
            s => s.GetSeriesOverviewPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<long?>()),
            Times.Never);
    }

    // Regression test for the series-section divisibility guard: the section pages by page
    // number (page = seriesOffset / seriesLimit), so an offset that is not a multiple of the
    // limit would silently truncate instead of faulting.
    [TestMethod]
    public async Task GetAuthorDetail_NonMultipleSeriesOffset_IsRefusedAs400()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));

        var result = await _controller.GetAuthorDetail(authorId: 7, seriesLimit: 50, seriesOffset: 30);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
        _seriesService.Verify(
            s => s.GetSeriesOverviewPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<long?>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetAuthorDetail_MultipleSeriesOffset_PassesValidationAndReachesTheService()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(1, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));

        var result = await _controller.GetAuthorDetail(authorId: 7, seriesLimit: 50, seriesOffset: 50);

        Assert.IsNotNull(result.Value);
        _seriesService.Verify(
            s => s.GetSeriesOverviewPageAsync(1, 50, null, null, 7), Times.Once,
            "a multiple seriesOffset must map to page seriesOffset / seriesLimit and reach the service");
    }

    [TestMethod]
    public async Task GetAuthorDetail_UnknownAuthor_Returns404WithoutCallingTheSectionQueries()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(999)).ReturnsAsync((AuthorSummaryRow?)null);

        var result = await _controller.GetAuthorDetail(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
        _seriesService.Verify(
            s => s.GetSeriesOverviewPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<long?>()),
            Times.Never);
        _audiobookRepo.Verify(
            r => r.GetStandaloneBooksByAuthorAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetAuthorFollowStatus_ReflectsTheServiceResult()
    {
        _upcomingReleaseService.Setup(s => s.IsAuthorFollowedAsync(7)).ReturnsAsync(true);

        var result = await _controller.GetAuthorFollowStatus(7);

        Assert.IsTrue(result.Value!.IsFollowed);
    }

    [TestMethod]
    public async Task FollowAuthor_KnownAuthor_ReturnsOk()
    {
        var result = await _controller.FollowAuthor(7);

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.FollowAuthorAsync(7), Times.Once);
    }

    [TestMethod]
    public async Task FollowAuthor_UnknownAuthor_Returns404()
    {
        _upcomingReleaseService.Setup(s => s.FollowAuthorAsync(999)).ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.FollowAuthor(999);

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task UnfollowAuthor_DelegatesToTheService()
    {
        var result = await _controller.UnfollowAuthor(7);

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.UnfollowAuthorAsync(7), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthorMatch_UnknownAuthor_Returns404()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(999)).ReturnsAsync((AuthorSummaryRow?)null);

        var result = await _controller.GetAuthorMatch(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task GetAuthorMatch_KnownAuthor_ReturnsTheStoredMatch()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _personRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson")
        {
            MatchedSourceId = "123",
            MatchedSourceName = "Hardcover",
            MatchedSourceUrl = "https://hardcover.app/authors/123",
        });

        var result = await _controller.GetAuthorMatch(7);

        Assert.AreEqual("123", result.Value!.SourceId);
        Assert.AreEqual("Hardcover", result.Value!.SourceName);
        Assert.AreEqual("https://hardcover.app/authors/123", result.Value!.SourceUrl);
    }

    [TestMethod]
    public async Task GetAuthorMatch_UnmatchedAuthor_ReturnsAllNullFields()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _personRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson"));

        var result = await _controller.GetAuthorMatch(7);

        Assert.IsNull(result.Value!.SourceId);
    }

    [TestMethod]
    public async Task GetAuthorMatchCandidates_UnknownAuthor_Returns404()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(999)).ReturnsAsync((AuthorSummaryRow?)null);

        var result = await _controller.GetAuthorMatchCandidates(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
        _upcomingReleaseService.Verify(
            s => s.SearchAuthorMatchCandidatesAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task GetAuthorMatchCandidates_NoQuery_SearchesByTheAuthorsOwnName()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _upcomingReleaseService.Setup(s => s.SearchAuthorMatchCandidatesAsync("Brandon Sanderson"))
            .ReturnsAsync(new List<AuthorSearchResult>());

        await _controller.GetAuthorMatchCandidates(7);

        _upcomingReleaseService.Verify(s => s.SearchAuthorMatchCandidatesAsync("Brandon Sanderson"), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthorMatchCandidates_ExplicitQuery_OverridesTheAuthorsName()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _upcomingReleaseService.Setup(s => s.SearchAuthorMatchCandidatesAsync("Sando"))
            .ReturnsAsync(new List<AuthorSearchResult> { new("123", "Brandon Sanderson") { Source = "Hardcover", BookCount = 40 } });

        var result = await _controller.GetAuthorMatchCandidates(7, "Sando");

        var candidate = result.Value!.Single();
        Assert.AreEqual("123", candidate.SourceId);
        Assert.AreEqual("Hardcover", candidate.SourceName);
        Assert.AreEqual(40, candidate.BookCount);
    }

    [TestMethod]
    public async Task GetAuthorMatchCandidates_DailyLimitExceeded_ReturnsInvalidRequestNotUnexpectedError()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _upcomingReleaseService.Setup(s => s.SearchAuthorMatchCandidatesAsync(It.IsAny<string>()))
            .ThrowsAsync(new HardcoverDailyLimitExceededException(5000));

        var result = await _controller.GetAuthorMatchCandidates(7);

        ProblemAssert.HasDetail(
            result.Result,
            StatusCodes.Status400BadRequest,
            "Hardcover daily request limit of 5000 requests has been reached for today (UTC). Further requests are blocked until the limit resets at UTC midnight.");
    }

    [TestMethod]
    public async Task MatchAuthor_BlankSourceId_ReturnsInvalidRequest()
    {
        var result = await _controller.MatchAuthor(7, new MatchAuthorDto("", "Hardcover", null));

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "SourceId and SourceName are required.");
        _upcomingReleaseService.Verify(
            s => s.MatchAuthorAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [TestMethod]
    public async Task MatchAuthor_NullBody_ReturnsInvalidRequest()
    {
        var result = await _controller.MatchAuthor(7, null);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "SourceId and SourceName are required.");
    }

    [TestMethod]
    public async Task MatchAuthor_ValidRequest_MatchesThenRefreshesTheRosterUnderTheSharedLock()
    {
        var result = await _controller.MatchAuthor(7, new MatchAuthorDto("123", "Hardcover", "https://hardcover.app/authors/123"));

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(
            s => s.MatchAuthorAsync(7, "123", "Hardcover", "https://hardcover.app/authors/123"), Times.Once);
        // The match persists first; the roster refresh then runs under the same author-refresh
        // lock as the explicit single/bulk refresh, so the newly-matched author's unified roster
        // is populated immediately.
        _upcomingReleaseService.Verify(s => s.RefreshAuthorRosterAsync(7), Times.Once);
    }

    // Matching triggers a refresh, so a refresh-already-in-progress refuses the match with the
    // same 409 the explicit refresh endpoints return - the match is NOT persisted in that case.
    [TestMethod]
    public async Task MatchAuthor_RefreshAlreadyRunning_ReturnsConflictWithoutMatching()
    {
        Assert.IsTrue(_expectedBookWriteGate.TryAcquire());

        try
        {
            var result = await _controller.MatchAuthor(7, new MatchAuthorDto("123", "Hardcover", null));

            ProblemAssert.HasDetail(result, StatusCodes.Status409Conflict, "An author-roster refresh is already in progress.");
            _upcomingReleaseService.Verify(
                s => s.MatchAuthorAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Never);
            _upcomingReleaseService.Verify(s => s.RefreshAuthorRosterAsync(It.IsAny<long>()), Times.Never);
        }
        finally
        {
            _expectedBookWriteGate.Release();
        }
    }

    // Regression: the match must persist even when the refresh that follows fails (e.g. the
    // source's daily budget) - the error surfaces, but the next periodic sweep picks the roster up.
    [TestMethod]
    public async Task MatchAuthor_RefreshFails_MatchStaysPersistedAndTheErrorSurfaces()
    {
        _upcomingReleaseService
            .Setup(s => s.RefreshAuthorRosterAsync(7))
            .ThrowsAsync(new HardcoverDailyLimitExceededException(5000));

        var result = await _controller.MatchAuthor(7, new MatchAuthorDto("123", "Hardcover", null));

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Hardcover daily request limit of 5000 requests has been reached for today (UTC). Further requests are blocked until the limit resets at UTC midnight.");
        _upcomingReleaseService.Verify(
            s => s.MatchAuthorAsync(7, "123", "Hardcover", null), Times.Once,
            "the match is stored BEFORE the refresh is attempted");
    }

    [TestMethod]
    public async Task MatchAuthor_UnknownAuthor_Returns404()
    {
        _upcomingReleaseService
            .Setup(s => s.MatchAuthorAsync(999, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.MatchAuthor(999, new MatchAuthorDto("123", "Hardcover", null));

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
        _upcomingReleaseService.Verify(s => s.RefreshAuthorRosterAsync(It.IsAny<long>()), Times.Never);
    }

    [TestMethod]
    public async Task UnmatchAuthor_DelegatesToTheService()
    {
        var result = await _controller.UnmatchAuthor(7);

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.UnmatchAuthorAsync(7), Times.Once);
    }

    [TestMethod]
    public async Task UnmatchAuthor_UnknownAuthor_Returns404()
    {
        _upcomingReleaseService.Setup(s => s.UnmatchAuthorAsync(999)).ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.UnmatchAuthor(999);

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task RefreshAuthor_Success_ReturnsMappedResult()
    {
        var refreshedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _personRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson") { LastRefreshedAt = refreshedAt });

        var result = await _controller.RefreshAuthor(7);

        Assert.IsTrue(result.Value!.Success);
        Assert.AreEqual(refreshedAt, result.Value!.LastRefreshedAt);
        _upcomingReleaseService.Verify(s => s.RefreshAuthorRosterAsync(7), Times.Once);
    }

    // Review finding 1: the single-author refresh and the bulk sweep share a gate, so a
    // sweep already running refuses a second refresh with 409 instead of letting both mutate the
    // same author's roster concurrently.
    [TestMethod]
    public async Task RefreshAuthor_RefreshAlreadyRunning_ReturnsConflict()
    {
        Assert.IsTrue(_expectedBookWriteGate.TryAcquire());

        try
        {
            var result = await _controller.RefreshAuthor(7);

            ProblemAssert.HasDetail(result.Result, StatusCodes.Status409Conflict, "An author-roster refresh is already in progress.");
            _upcomingReleaseService.Verify(s => s.RefreshAuthorRosterAsync(It.IsAny<long>()), Times.Never);
        }
        finally
        {
            _expectedBookWriteGate.Release();
        }
    }

    [TestMethod]
    public async Task RefreshAuthor_ReleasesTheRefreshGate()
    {
        _personRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(new Person(7, "Brandon Sanderson"));

        await _controller.RefreshAuthor(7);

        Assert.IsTrue(_expectedBookWriteGate.TryAcquire(), "a single refresh must not leave the gate held");
        _expectedBookWriteGate.Release();
    }

    // Review finding 2: refresh-all used to run synchronously on the request thread (one
    // rate-limited call per matched author, potentially minutes). It is now fire-and-forget,
    // mirroring SeriesController.StartRefreshAllSeries: it returns immediately and the work runs
    // in the background.
    [TestMethod]
    public async Task RefreshAllAuthors_ReturnsOkImmediately_AndRunsInTheBackground()
    {
        _upcomingReleaseService.Setup(s => s.RefreshAllAuthorRostersAsync())
            .ReturnsAsync((3, 3, 0, (string?)null));

        var finished = RegisterFinishedWaiter(BrowseController.RefreshAllOperationKey);

        var result = _controller.RefreshAllAuthors();

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished, _expectedBookWriteGate);

        _upcomingReleaseService.Verify(s => s.RefreshAllAuthorRostersAsync(), Times.Once);
    }

    [TestMethod]
    public async Task RefreshAllAuthors_AlreadyRunning_ReturnsConflict()
    {
        var release = new TaskCompletionSource();
        _upcomingReleaseService.Setup(s => s.RefreshAllAuthorRostersAsync())
            .Returns(async () =>
            {
                await release.Task;
                return (1, 1, 0, (string?)null);
            });

        var first = _controller.RefreshAllAuthors();
        Assert.IsInstanceOfType(first, typeof(OkResult));

        var second = _controller.RefreshAllAuthors();
        ProblemAssert.HasStatus(second, StatusCodes.Status409Conflict);

        var finished = RegisterFinishedWaiter(BrowseController.RefreshAllOperationKey);
        release.SetResult();
        await AwaitOperationFinished(finished, _expectedBookWriteGate);
    }

    // The single-author refresh and the bulk sweep take the SAME static gate, so a sweep already
    // running also refuses a concurrent single-author refresh (not just a second sweep).
    [TestMethod]
    public async Task RefreshAllAuthors_Running_BlocksASingleAuthorRefreshToo()
    {
        var release = new TaskCompletionSource();
        _upcomingReleaseService.Setup(s => s.RefreshAllAuthorRostersAsync())
            .Returns(async () =>
            {
                await release.Task;
                return (1, 1, 0, (string?)null);
            });

        var sweep = _controller.RefreshAllAuthors();
        Assert.IsInstanceOfType(sweep, typeof(OkResult));

        var single = await _controller.RefreshAuthor(7);
        ProblemAssert.HasDetail(single.Result, StatusCodes.Status409Conflict, "An author-roster refresh is already in progress.");

        var finished = RegisterFinishedWaiter(BrowseController.RefreshAllOperationKey);
        release.SetResult();
        await AwaitOperationFinished(finished, _expectedBookWriteGate);
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_BlankTitleAndId_ReturnsInvalidRequest()
    {
        var result = await _controller.IgnoreExpectedBook(7, new AuthorExpectedBookRefDto { Title = " " });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Expected book Id or Title is required to identify the expected book.");
        _upcomingReleaseService.Verify(
            s => s.DismissAuthorRosterUpcomingAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_ValidTitle_SetsIgnoredTrueOnTheSharedRow()
    {
        var result = await _controller.IgnoreExpectedBook(7, new AuthorExpectedBookRefDto { Title = "Elantris" });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.DismissAuthorRosterUpcomingAsync(7, "Elantris"), Times.Once);
    }

    // The stable expected-book row id is the preferred addressing - a person can carry two
    // same-titled roster entries, which the title route cannot tell apart. The id route is
    // unconditional (no per-author title check) because the row id identifies the exact shared
    // row.
    [TestMethod]
    public async Task IgnoreExpectedBook_ById_DismissesTheExactRow()
    {
        var result = await _controller.IgnoreExpectedBook(7, new AuthorExpectedBookRefDto { Id = 42, Title = "Elantris" });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.DismissAuthorRosterUpcomingByIdAsync(42), Times.Once);
        _upcomingReleaseService.Verify(
            s => s.DismissAuthorRosterUpcomingAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never,
            "a body carrying the id must not fall back to the title route");
    }

    [TestMethod]
    public async Task IgnoreExpectedBook_UnknownId_Returns404()
    {
        _upcomingReleaseService.Setup(s => s.DismissAuthorRosterUpcomingByIdAsync(999))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.IgnoreExpectedBook(7, new AuthorExpectedBookRefDto { Id = 999 });

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task UnignoreExpectedBook_ValidTitle_SetsIgnoredFalseOnTheSharedRow()
    {
        var result = await _controller.UnignoreExpectedBook(7, new AuthorExpectedBookRefDto { Title = "Elantris" });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.RestoreAuthorRosterUpcomingAsync(7, "Elantris"), Times.Once);
    }

    [TestMethod]
    public async Task UnignoreExpectedBook_ById_RestoresTheExactRow()
    {
        var result = await _controller.UnignoreExpectedBook(7, new AuthorExpectedBookRefDto { Id = 42 });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(s => s.RestoreAuthorRosterUpcomingByIdAsync(42), Times.Once);
    }

    [TestMethod]
    public async Task UnignoreExpectedBook_UnknownEntry_Returns404()
    {
        _upcomingReleaseService.Setup(s => s.RestoreAuthorRosterUpcomingAsync(7, "Nonexistent"))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.UnignoreExpectedBook(7, new AuthorExpectedBookRefDto { Title = "Nonexistent" });

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task GetAuthorDetail_MapsIgnoredBooksSection()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));
        _authorReconciliation.Setup(r => r.GetReconciliationAsync(7, It.IsAny<bool>())).ReturnsAsync(
            new AuthorReconciliation(
                Missing: new List<AuthorExpectedBookInfo>(),
                Upcoming: new List<AuthorExpectedBookInfo>(),
                Ignored: new List<AuthorExpectedBookInfo> { new() { Id = 1, Title = "Warbreaker", IsIgnored = true } },
                ExpectedBookCount: 0,
                OwnedCount: 0,
                MissingSeries: new List<AuthorMissingSeriesInfo>()));

        var result = await _controller.GetAuthorDetail(7);

        var ignored = result.Value!.IgnoredBooks!.Single();
        Assert.AreEqual("Warbreaker", ignored.Title);
        Assert.IsTrue(ignored.IsIgnored);
    }

    // The missing-series section is opt-in: the reconciliation pass that computes it is per-author
    // work the default detail call has no need for, so it is skipped unless the caller asks.
    [TestMethod]
    public async Task GetAuthorDetail_MissingSeries_NotComputedByDefault()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));

        var result = await _controller.GetAuthorDetail(7);

        Assert.IsNotNull(result.Value);
        Assert.IsNull(result.Value!.MissingSeries);
        _authorReconciliation.Verify(r => r.GetReconciliationAsync(7, false), Times.Once,
            "the default detail call must not pay for the missing-series reconciliation pass");
    }

    [TestMethod]
    public async Task GetAuthorDetail_MissingSeries_IncludedWhenRequested_WithCountsAndMappedFields()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));
        _authorReconciliation.Setup(r => r.GetReconciliationAsync(7, true)).ReturnsAsync(
            new AuthorReconciliation(
                Missing: new List<AuthorExpectedBookInfo>(),
                Upcoming: new List<AuthorExpectedBookInfo>(),
                Ignored: new List<AuthorExpectedBookInfo>(),
                ExpectedBookCount: 4,
                OwnedCount: 2,
                MissingSeries: new List<AuthorMissingSeriesInfo>
                {
                    new()
                    {
                        SourceName = "Hardcover",
                        SourceSeriesId = "55",
                        SourceSeriesName = "The Stormlight Archive",
                        SeriesId = 9,
                        SeriesName = "The Stormlight Archive",
                        ExpectedCount = 5,
                        MissingCount = 3,
                        UpcomingCount = 2,
                        OwnedCount = 0,
                    },
                    new()
                    {
                        SourceName = "Hardcover",
                        SourceSeriesId = "99",
                        SourceSeriesName = "Skyward",
                        ExpectedCount = 1,
                        MissingCount = 1,
                        UpcomingCount = 0,
                        OwnedCount = 0,
                    },
                }));

        var result = await _controller.GetAuthorDetail(7, includeMissingSeries: true);

        var ok = result.Value!;
        Assert.IsNotNull(ok.MissingSeries);
        Assert.AreEqual(2, ok.MissingSeries.Total, "the total is the full group count, not the page");
        Assert.AreEqual(2, ok.MissingSeries.Count);
        var matched = ok.MissingSeries.Items[0];
        Assert.AreEqual("Hardcover", matched.SourceName);
        Assert.AreEqual("55", matched.SourceSeriesId);
        Assert.AreEqual("The Stormlight Archive", matched.SourceSeriesName);
        Assert.AreEqual(5, matched.ExpectedCount);
        Assert.AreEqual(3, matched.MissingCount);
        Assert.AreEqual(2, matched.UpcomingCount);
        Assert.AreEqual(0, matched.OwnedBookCount);
        Assert.AreEqual(9, matched.MatchedSeriesId);
        Assert.AreEqual("The Stormlight Archive", matched.MatchedSeriesName);
        Assert.IsNull(ok.MissingSeries.Items[1].MatchedSeriesId,
            "an unmatched source series has no matched local series id");
        _authorReconciliation.Verify(r => r.GetReconciliationAsync(7, true), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthorDetail_MissingSeries_AppliesItsOwnPagingParams()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));
        _authorReconciliation.Setup(r => r.GetReconciliationAsync(7, true)).ReturnsAsync(
            new AuthorReconciliation(
                Missing: new List<AuthorExpectedBookInfo>(),
                Upcoming: new List<AuthorExpectedBookInfo>(),
                Ignored: new List<AuthorExpectedBookInfo>(),
                ExpectedBookCount: 3,
                OwnedCount: 0,
                MissingSeries: new List<AuthorMissingSeriesInfo>
                {
                    new() { SourceName = "Hardcover", SourceSeriesId = "1", SourceSeriesName = "First", ExpectedCount = 1, MissingCount = 1, UpcomingCount = 0, OwnedCount = 0 },
                    new() { SourceName = "Hardcover", SourceSeriesId = "2", SourceSeriesName = "Second", ExpectedCount = 1, MissingCount = 1, UpcomingCount = 0, OwnedCount = 0 },
                    new() { SourceName = "Hardcover", SourceSeriesId = "3", SourceSeriesName = "Third", ExpectedCount = 1, MissingCount = 1, UpcomingCount = 0, OwnedCount = 0 },
                }));

        var result = await _controller.GetAuthorDetail(
            authorId: 7,
            includeMissingSeries: true,
            missingSeriesLimit: 2,
            missingSeriesOffset: 1);

        var ok = result.Value!;
        Assert.IsNotNull(ok.MissingSeries);
        Assert.AreEqual(2, ok.MissingSeries.Count);
        Assert.AreEqual(3, ok.MissingSeries.Total, "the total is the full group count, not the page");
        Assert.AreEqual("Second", ok.MissingSeries.Items[0].SourceSeriesName, "the offset slices into the list");
    }

    [TestMethod]
    public async Task GetAuthorDetail_AnOutOfRangeMissingSeriesLimit_IsRefused()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));

        var result = await _controller.GetAuthorDetail(authorId: 7, missingSeriesLimit: 0);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
        _authorReconciliation.Verify(
            r => r.GetReconciliationAsync(It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task GetAuthorDetail_AnOutOfRangeMissingSeriesOffset_IsRefused()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));

        var result = await _controller.GetAuthorDetail(authorId: 7, missingSeriesOffset: 1_000_001);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
        _authorReconciliation.Verify(
            r => r.GetReconciliationAsync(It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
    }

    // The unified roster rows now carry their series context onto the wire, so the author detail
    // can render a series-linked bibliography entry with its position and matched/source series
    // names without a second lookup.
    [TestMethod]
    public async Task GetAuthorDetail_MissingBooksSection_CarriesSeriesContext()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _seriesService.Setup(s => s.GetSeriesOverviewPageAsync(0, 50, null, null, 7))
            .ReturnsAsync(new SeriesOverviewPage { Items = new List<SeriesOverview>(), TotalCount = 0 });
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 0));
        _authorReconciliation.Setup(r => r.GetReconciliationAsync(7, It.IsAny<bool>())).ReturnsAsync(
            new AuthorReconciliation(
                Missing: new List<AuthorExpectedBookInfo>
                {
                    new()
                    {
                        Id = 5,
                        Title = "Words of Radiance",
                        Year = 2030,
                        SourceUrl = "https://hardcover.app/books/555",
                        IsIgnored = false,
                        ReleaseDate = new DateOnly(2030, 1, 1),
                        SourceName = "Hardcover",
                        SourceBookId = "555",
                        ImageUrl = "https://covers.hardcover.app/words-of-radiance.jpg",
                        Position = "2",
                        SeriesId = 9,
                        SeriesName = "The Stormlight Archive",
                        SourceSeriesId = "55",
                        SourceSeriesName = "The Stormlight Archive",
                    },
                },
                Upcoming: new List<AuthorExpectedBookInfo>(),
                Ignored: new List<AuthorExpectedBookInfo>(),
                ExpectedBookCount: 1,
                OwnedCount: 0,
                MissingSeries: new List<AuthorMissingSeriesInfo>()));

        var result = await _controller.GetAuthorDetail(7);

        var book = result.Value!.MissingBooks!.Single();
        Assert.AreEqual(5, book.Id);
        Assert.AreEqual("2", book.Position);
        Assert.AreEqual(9, book.SeriesId);
        Assert.AreEqual("The Stormlight Archive", book.SeriesName);
        Assert.AreEqual("The Stormlight Archive", book.SourceSeriesName);
        Assert.AreEqual("Hardcover", book.SourceName);
        Assert.AreEqual("555", book.SourceBookId);
        Assert.AreEqual("https://covers.hardcover.app/words-of-radiance.jpg", book.ImageUrl);
    }
}
