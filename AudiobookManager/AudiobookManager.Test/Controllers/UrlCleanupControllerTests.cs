using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class UrlCleanupControllerTests
{
    private Mock<IUrlCleanupService> _urlCleanupService = null!;
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<ILogger<UrlCleanupController>> _logger = null!;
    private UrlCleanupController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _urlCleanupService = new Mock<IUrlCleanupService>();
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _logger = new Mock<ILogger<UrlCleanupController>>();

        _controller = new UrlCleanupController(
            _urlCleanupService.Object,
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            Mock.Of<IHostApplicationLifetime>(),
            _logger.Object);
    }

    private void SetupScope(IUrlCleanupService service)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IUrlCleanupService)))
            .Returns(service);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
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

    // Apply-all is fire-and-forget through BackgroundOperationRunner: the response only says the
    // operation started, and progress/completion travel over SignalR (UrlCleanupProgress/Complete),
    // mirroring the consistency bulk-resolve flow.

    [TestMethod]
    public async Task ApplyAll_Valid_StartsTheRun_AndReportsProgressAndCompletion()
    {
        var clientProxy = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var mockCleanupService = new Mock<IUrlCleanupService>();
        mockCleanupService.Setup(s => s.ApplyAllAsync(It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 2, 1, 0).GetAwaiter().GetResult();
                return (2, 2, 0);
            });
        SetupScope(mockCleanupService.Object);

        var result = _controller.ApplyAll();

        Assert.IsInstanceOfType<OkResult>(result);
        _statusRegistry.Verify(s => s.SetRunning("url-cleanup-apply"), Times.Once);

        await OperationGate.WaitUntilReleasedAsync(typeof(UrlCleanupController));

        mockCleanupService.Verify(s => s.ApplyAllAsync(It.IsAny<Func<int, int, int, int, Task>>()), Times.Once);
        clientProxy.Verify(c => c.UrlCleanupProgress(It.Is<UrlCleanupProgress>(p =>
            p.Processed == 1 && p.Total == 2 && p.Succeeded == 1 && p.Failed == 0)), Times.Once);
        clientProxy.Verify(c => c.UrlCleanupComplete(It.Is<UrlCleanupComplete>(r =>
            r.TotalProcessed == 2 && r.TotalSucceeded == 2 && r.TotalFailed == 0)), Times.Once);
        _statusRegistry.Verify(s => s.SetFinished("url-cleanup-apply"), Times.Once);
    }

    [TestMethod]
    public async Task ApplyAll_ALoadedSweep_IsA409_OneSweepAtATime()
    {
        // The gate is process-static: hold the first operation open with a completeness signal
        // rather than a fixed sleep, so the second call provably hits a held gate.
        var workMayFinish = new TaskCompletionSource();
        var mockCleanupService = new Mock<IUrlCleanupService>();
        mockCleanupService.Setup(s => s.ApplyAllAsync(It.IsAny<Func<int, int, int, int, Task>?>()))
            .Returns(async () =>
            {
                await workMayFinish.Task;
                return (0, 0, 0);
            });
        SetupScope(mockCleanupService.Object);

        var first = _controller.ApplyAll();
        Assert.IsInstanceOfType<OkResult>(first);

        var second = _controller.ApplyAll();
        Assert.AreEqual(StatusCodes.Status409Conflict, ((ObjectResult)second).StatusCode);

        // Release the first operation so the gate is free for later tests.
        workMayFinish.SetResult();
        await OperationGate.WaitUntilReleasedAsync(typeof(UrlCleanupController));
    }
}