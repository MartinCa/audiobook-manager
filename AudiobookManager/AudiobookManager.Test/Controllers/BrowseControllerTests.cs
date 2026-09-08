using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class BrowseControllerTests
{
    private Mock<IAudiobookRepository> _audiobookRepo = null!;
    private Mock<IPersonRepository> _personRepo = null!;
    private BrowseController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepo = new Mock<IAudiobookRepository>();
        _personRepo = new Mock<IPersonRepository>();
        _controller = new BrowseController(_audiobookRepo.Object, _personRepo.Object);
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
        _audiobookRepo.Setup(r => r.GetSeriesCountsByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<(string Series, int BookCount)> { ("Mistborn", 3) }, 2));
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 50, 0))
            .ReturnsAsync((new List<Audiobook>(), 4));

        var result = await _controller.GetAuthorDetail(7);

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        Assert.AreEqual(7, ok.Author.Id);
        Assert.AreEqual(1, ok.Series.Count);
        Assert.AreEqual(2, ok.Series.Total);
        Assert.AreEqual("Mistborn", ok.Series.Items[0].SeriesName);
        Assert.AreEqual(3, ok.Series.Items[0].BookCount);
        Assert.AreEqual(0, ok.StandaloneBooks.Count);
        Assert.AreEqual(4, ok.StandaloneBooks.Total);
    }

    [TestMethod]
    public async Task GetAuthorDetail_PassesSectionPagingThrough()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));
        _audiobookRepo.Setup(r => r.GetSeriesCountsByAuthorAsync(7, 25, 50))
            .ReturnsAsync((new List<(string Series, int BookCount)>(), 0));
        _audiobookRepo.Setup(r => r.GetStandaloneBooksByAuthorAsync(7, 10, 20))
            .ReturnsAsync((new List<Audiobook>(), 0));

        var result = await _controller.GetAuthorDetail(
            authorId: 7, seriesLimit: 25, seriesOffset: 50, standaloneLimit: 10, standaloneOffset: 20);

        var ok = result.Value!;
        Assert.IsNotNull(ok);
        _audiobookRepo.Verify(r => r.GetSeriesCountsByAuthorAsync(7, 25, 50), Times.Once);
        _audiobookRepo.Verify(r => r.GetStandaloneBooksByAuthorAsync(7, 10, 20), Times.Once);
    }

    [TestMethod]
    public async Task GetAuthorDetail_AnInvalidSectionLimit_IsRefusedWithoutCallingTheRepository()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(7)).ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));

        var result = await _controller.GetAuthorDetail(authorId: 7, standaloneLimit: 0);

        Assert.AreEqual(400, ((ObjectResult)result.Result!).StatusCode);
        _audiobookRepo.Verify(
            r => r.GetSeriesCountsByAuthorAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetAuthorDetail_UnknownAuthor_Returns404WithoutCallingTheSectionQueries()
    {
        _personRepo.Setup(r => r.GetAuthorSummaryAsync(999)).ReturnsAsync((AuthorSummaryRow?)null);

        var result = await _controller.GetAuthorDetail(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
        _audiobookRepo.Verify(
            r => r.GetSeriesCountsByAuthorAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
        _audiobookRepo.Verify(
            r => r.GetStandaloneBooksByAuthorAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }
}
