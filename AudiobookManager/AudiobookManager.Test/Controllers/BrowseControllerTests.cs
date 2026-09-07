using AudiobookManager.Api.Controllers;
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
}
