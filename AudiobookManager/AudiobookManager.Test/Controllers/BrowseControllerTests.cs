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
            null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);

    [TestMethod]
    public async Task CombinedSearch_BlankQuery_ReturnsEmptyResult()
    {
        var result = await _controller.CombinedSearch("   ");

        Assert.AreEqual(0, result.Books.Count);
        Assert.AreEqual(0, result.Authors.Count);
        Assert.AreEqual(0, result.Series.Count);
        _audiobookRepo.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task CombinedSearch_CombinesBooksAuthorsAndSeries()
    {
        var book = MakeBook(1, "Mistborn: The Final Empire", "Mistborn");
        book.Authors = new List<Person> { new(1, "Brandon Sanderson") };

        _audiobookRepo.Setup(r => r.SearchAsync("mist", 5, 0))
            .ReturnsAsync((new List<Audiobook> { book }, 1));
        _personRepo.Setup(r => r.SearchAuthorsAsync("mist", 5))
            .ReturnsAsync(new List<Person>());
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("mist", 5))
            .ReturnsAsync(new List<(string Series, int BookCount)> { ("Mistborn", 3) });

        var result = await _controller.CombinedSearch("mist");

        Assert.AreEqual(1, result.Books.Count);
        Assert.AreEqual("Mistborn: The Final Empire", result.Books[0].BookName);
        CollectionAssert.Contains(result.Books[0].Authors, "Brandon Sanderson");
        Assert.AreEqual(1, result.Series.Count);
        Assert.AreEqual("Mistborn", result.Series[0].Name);
        Assert.AreEqual(3, result.Series[0].BookCount);
        Assert.AreEqual(0, result.Authors.Count);
    }

    [TestMethod]
    public async Task CombinedSearch_RanksExactPrefixMatchesFirst()
    {
        var prefixMatch = new Person(1, "San Diego") { BooksAuthored = new List<Audiobook> { MakeBook(1, "Book A") } };
        var substringMatch = new Person(2, "Brandon Sanderson") { BooksAuthored = new List<Audiobook> { MakeBook(2, "Book B") } };

        _audiobookRepo.Setup(r => r.SearchAsync("san", 5, 0)).ReturnsAsync((new List<Audiobook>(), 0));
        _audiobookRepo.Setup(r => r.SearchSeriesAsync("san", 5)).ReturnsAsync(new List<(string Series, int BookCount)>());
        _personRepo.Setup(r => r.SearchAuthorsAsync("san", 5))
            .ReturnsAsync(new List<Person> { substringMatch, prefixMatch });

        var result = await _controller.CombinedSearch("san");

        Assert.AreEqual(2, result.Authors.Count);
        Assert.AreEqual("San Diego", result.Authors[0].Name);
        Assert.AreEqual("Brandon Sanderson", result.Authors[1].Name);
    }
}
