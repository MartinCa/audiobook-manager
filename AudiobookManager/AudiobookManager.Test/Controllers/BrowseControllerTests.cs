using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private Mock<ISeriesService> _seriesService = null!;
    private Mock<IUpcomingReleaseService> _upcomingReleaseService = null!;
    private BrowseController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepo = new Mock<IAudiobookRepository>();
        _personRepo = new Mock<IPersonRepository>();
        _seriesService = new Mock<ISeriesService>();
        _upcomingReleaseService = new Mock<IUpcomingReleaseService>();
        _controller = new BrowseController(
            _audiobookRepo.Object, _personRepo.Object, _seriesService.Object,
            _upcomingReleaseService.Object, Mock.Of<ILogger<BrowseController>>());
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
            HardcoverAuthorId = "123",
            HardcoverAuthorName = "Hardcover",
            HardcoverAuthorUrl = "https://hardcover.app/authors/123",
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
    public async Task MatchAuthor_ValidRequest_CallsTheServiceAndReturnsOk()
    {
        var result = await _controller.MatchAuthor(7, new MatchAuthorDto("123", "Hardcover", "https://hardcover.app/authors/123"));

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _upcomingReleaseService.Verify(
            s => s.MatchAuthorAsync(7, "123", "Hardcover", "https://hardcover.app/authors/123"), Times.Once);
    }

    [TestMethod]
    public async Task MatchAuthor_UnknownAuthor_Returns404()
    {
        _upcomingReleaseService
            .Setup(s => s.MatchAuthorAsync(999, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.MatchAuthor(999, new MatchAuthorDto("123", "Hardcover", null));

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
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
}
