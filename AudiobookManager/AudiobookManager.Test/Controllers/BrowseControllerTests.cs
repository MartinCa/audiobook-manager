using AudiobookManager.Api.Controllers;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
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
        _audiobookRepo.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task SearchLibrary_CombinesBooksAuthorsAndSeries()
    {
        var book = MakeBook(1, "Mistborn: The Final Empire", "Mistborn");
        book.Authors = new List<Person> { new(1, "Brandon Sanderson") };

        _audiobookRepo.Setup(r => r.SearchAsync("mist", 5, 0))
            .ReturnsAsync((new List<Audiobook> { book }, 1));
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("mist", 5))
            .ReturnsAsync(new List<AuthorSummaryRow>());
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("mist", 5))
            .ReturnsAsync(new List<(string Series, int BookCount)> { ("Mistborn", 3) });

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

        _audiobookRepo.Setup(r => r.SearchAsync("san", 5, 0)).ReturnsAsync((new List<Audiobook>(), 0));
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("san", 5)).ReturnsAsync(new List<(string Series, int BookCount)>());
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("san", 5))
            .ReturnsAsync(new List<AuthorSummaryRow> { prefixMatch, substringMatch });

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

        _audiobookRepo.Setup(r => r.SearchAsync("rene", 5, 0)).ReturnsAsync((new List<Audiobook>(), 0));
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("rene", 5)).ReturnsAsync(new List<(string Series, int BookCount)>());
        _personRepo.Setup(r => r.SearchAuthorSummariesAsync("rene", 5))
            .ReturnsAsync(new List<AuthorSummaryRow> { accentedPrefixMatch, substringMatch });

        var result = await _controller.SearchLibrary("rene");

        Assert.AreEqual(2, result.Authors.Count);
        Assert.AreEqual("Réne Girard", result.Authors[0].Name);
        Assert.AreEqual("Marie Irene", result.Authors[1].Name);
    }
}
