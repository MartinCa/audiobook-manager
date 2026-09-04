using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class UrlCleanupControllerTests
{
    private Mock<IUrlCleanupService> _urlCleanupService = null!;
    private UrlCleanupController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _urlCleanupService = new Mock<IUrlCleanupService>();
        _controller = new UrlCleanupController(_urlCleanupService.Object);
    }

    private static AudiobookUrlCleanup MakeCleanup(long id, string bookName, string currentUrl)
    {
        return new AudiobookUrlCleanup(id, bookName, new List<string>(), currentUrl, "https://cleaned.example/x");
    }

    [TestMethod]
    public async Task GetDirtyUrls_ReturnsPageDtoWithItemsAndTotal()
    {
        _urlCleanupService
            .Setup(s => s.FindDirtyUrlsPageAsync(0, 50))
            .ReturnsAsync((new List<AudiobookUrlCleanup> { MakeCleanup(1, "Book One", "https://dirty.example/x?ref=1") }, 2500));

        var result = await _controller.GetDirtyUrls();

        var page = ((OkObjectResult)result.Result!).Value as UrlCleanupPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(2500, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(1, page.Items[0].AudiobookId);
        Assert.AreEqual("Book One", page.Items[0].BookName);
        Assert.AreEqual("https://dirty.example/x?ref=1", page.Items[0].CurrentUrl);
        Assert.AreEqual("https://cleaned.example/x", page.Items[0].CleanedUrl);
    }

    [TestMethod]
    public async Task GetDirtyUrls_PassesPageAndPageSizeThrough()
    {
        _urlCleanupService
            .Setup(s => s.FindDirtyUrlsPageAsync(4, 25))
            .ReturnsAsync((new List<AudiobookUrlCleanup>(), 3699));

        var result = await _controller.GetDirtyUrls(page: 4, pageSize: 25);

        var page = ((OkObjectResult)result.Result!).Value as UrlCleanupPageDto;
        Assert.IsNotNull(page);
        // The total is the whole matching set, not the page - it is what sizes the pager.
        Assert.AreEqual(3699, page.TotalCount);
        _urlCleanupService.Verify(s => s.FindDirtyUrlsPageAsync(4, 25), Times.Once);
    }

    [TestMethod]
    [DataRow(-1, 50)]
    [DataRow(0, 0)]
    [DataRow(0, 201)]
    public async Task GetDirtyUrls_AnOutOfRangePage_IsRefused(int page, int pageSize)
    {
        var result = await _controller.GetDirtyUrls(page: page, pageSize: pageSize);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _urlCleanupService.Verify(
            s => s.FindDirtyUrlsPageAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    // Regression: the offset was computed as `page * pageSize` in a 32-bit int, so a large enough
    // page wrapped negative. That did not fail - SQLite reads a negative OFFSET as zero, so the
    // request silently returned the *first* page while claiming to be page eleven million.
    [TestMethod]
    public async Task GetDirtyUrls_APageLargeEnoughToOverflowTheOffset_IsRefusedRatherThanServingTheFirstPage()
    {
        var result = await _controller.GetDirtyUrls(page: 11_000_000, pageSize: 200);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _urlCleanupService.Verify(
            s => s.FindDirtyUrlsPageAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetDirtyUrls_APageBeyondTheOffsetCapButWithinIntRange_IsAlsoRefused()
    {
        // Not an overflow, just further than anyone can meaningfully page - and far enough that
        // the database would count its way there row by row.
        var result = await _controller.GetDirtyUrls(page: 50_000, pageSize: 50);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
    }

    [TestMethod]
    public async Task GetDirtyUrls_APageAtTheOffsetCap_IsStillAccepted()
    {
        _urlCleanupService
            .Setup(s => s.FindDirtyUrlsPageAsync(20_000, 50))
            .ReturnsAsync((new List<AudiobookUrlCleanup>(), 0));

        var result = await _controller.GetDirtyUrls(page: 20_000, pageSize: 50);

        Assert.IsInstanceOfType<OkObjectResult>(result.Result);
    }

    [TestMethod]
    public async Task GetDirtyUrlCount_ReturnsTheCount()
    {
        _urlCleanupService.Setup(s => s.CountDirtyUrlsAsync()).ReturnsAsync(2500);

        var count = await _controller.GetDirtyUrlCount();

        Assert.AreEqual(2500, count);
    }
}