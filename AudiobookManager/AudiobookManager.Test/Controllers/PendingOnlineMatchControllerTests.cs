using Microsoft.AspNetCore.Http;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class PendingOnlineMatchControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IOrganize> _clientProxy = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<IPendingOnlineMatchService> _service = null!;
    private Mock<ILogger<PendingOnlineMatchController>> _logger = null!;
    private PendingOnlineMatchController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _clientProxy = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _service = new Mock<IPendingOnlineMatchService>();
        _logger = new Mock<ILogger<PendingOnlineMatchController>>();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IPendingOnlineMatchService)))
            .Returns(_service.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _controller = new PendingOnlineMatchController(
            _service.Object,
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            Mock.Of<IHostApplicationLifetime>(),
            _logger.Object);
    }

    // See MetadataRefreshControllerTests' identical helper for why RunContinuationsAsynchronously
    // and the extra gate wait are both needed.
    private Task RegisterFinishedWaiter()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry.Setup(r => r.SetFinished(PendingOnlineMatchController.SearchOperationKey)).Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private static async Task AwaitOperationFinished(Task finishedSignal)
    {
        await finishedSignal.WaitAsync(TimeSpan.FromSeconds(5));
        await OperationGate.WaitUntilReleasedAsync(typeof(PendingOnlineMatchController));
    }

    #region StartSearchSelected

    [TestMethod]
    public void StartSearchSelected_EmptySelection_IsA400_NothingStarts()
    {
        var result = _controller.StartSearchSelected(new BulkOnlineMatchSearchDto
        {
            AudiobookIds = new List<long>(),
            SourceNames = new List<string> { "Audible" },
        });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "At least one audiobook must be selected.");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [TestMethod]
    public void StartSearchSelected_MoreThanTheCap_IsA400NamingTheCap()
    {
        var ids = new List<long>();
        for (var i = 0; i < 101; i++)
        {
            ids.Add(i + 1);
        }

        var result = _controller.StartSearchSelected(new BulkOnlineMatchSearchDto
        {
            AudiobookIds = ids,
            SourceNames = new List<string> { "Audible" },
        });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "No more than 100 audiobooks can be selected at once.");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [TestMethod]
    public void StartSearchSelected_NoSourceNames_IsA400_NothingStarts()
    {
        var result = _controller.StartSearchSelected(new BulkOnlineMatchSearchDto
        {
            AudiobookIds = new List<long> { 1 },
            SourceNames = new List<string>(),
        });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "At least one metadata source must be selected.");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [TestMethod]
    public void StartSearchSelected_NullSourceNames_IsA400()
    {
        var result = _controller.StartSearchSelected(new BulkOnlineMatchSearchDto
        {
            AudiobookIds = new List<long> { 1 },
            SourceNames = null!,
        });

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "At least one metadata source must be selected.");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [TestMethod]
    public async Task StartSearchSelected_Valid_ReturnsOkAndWiresProgressAndCompletion()
    {
        _service.Setup(s => s.SearchSelectedAsync(
                It.IsAny<IReadOnlyList<long>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((IReadOnlyList<long> _, IReadOnlyList<string> _, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 2, 1, 0).GetAwaiter().GetResult();
                return new PendingOnlineMatchBatchResult(2, 2, 2, 0);
            });

        var finished = RegisterFinishedWaiter();

        var result = _controller.StartSearchSelected(new BulkOnlineMatchSearchDto
        {
            AudiobookIds = new List<long> { 1, 2 },
            SourceNames = new List<string> { "Audible" },
        });

        Assert.IsInstanceOfType<OkResult>(result);

        await AwaitOperationFinished(finished);

        _service.Verify(s => s.SearchSelectedAsync(
            It.Is<IReadOnlyList<long>>(ids => ids.Count == 2),
            It.Is<IReadOnlyList<string>>(sources => sources.Contains("Audible")),
            It.IsAny<Func<int, int, int, int, Task>>()), Times.Once);
        _clientProxy.Verify(c => c.PendingOnlineMatchSearchProgress(It.Is<PendingOnlineMatchSearchProgress>(p =>
            p.Processed == 1 && p.Total == 2 && p.Succeeded == 1)), Times.Once);
        _clientProxy.Verify(c => c.PendingOnlineMatchSearchComplete(It.Is<PendingOnlineMatchSearchComplete>(r =>
            r.TotalProcessed == 2 && r.Total == 2 && r.TotalSucceeded == 2 && r.TotalFailed == 0)), Times.Once);
    }

    #endregion

    #region SelectResult

    [TestMethod]
    public async Task SelectResult_NullBody_IsA400()
    {
        var result = await _controller.SelectResult(1, null);

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "resultIndex is required.");
        _service.Verify(s => s.SelectResultAsync(It.IsAny<long>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task SelectResult_Valid_CallsServiceAndReturnsOk()
    {
        _service.Setup(s => s.SelectResultAsync(5, 0)).Returns(Task.CompletedTask);

        var result = await _controller.SelectResult(5, new SelectOnlineMatchResultDto(0));

        Assert.IsInstanceOfType<OkResult>(result);
        _service.Verify(s => s.SelectResultAsync(5, 0), Times.Once);
    }

    [TestMethod]
    public async Task SelectResult_NoRowForThisBook_Returns400()
    {
        _service.Setup(s => s.SelectResultAsync(6, 0))
            .ThrowsAsync(new KeyNotFoundException("Audiobook 6 has no pending online match."));

        var result = await _controller.SelectResult(6, new SelectOnlineMatchResultDto(0));

        ProblemAssert.HasDetail(result, StatusCodes.Status400BadRequest, "Audiobook 6 has no pending online match.");
    }

    [TestMethod]
    public async Task SelectResult_IndexOutOfRange_Returns400()
    {
        _service.Setup(s => s.SelectResultAsync(7, 5))
            .ThrowsAsync(new ArgumentOutOfRangeException("resultIndex", 5, "Audiobook 7 has 1 candidate result(s)."));

        var result = await _controller.SelectResult(7, new SelectOnlineMatchResultDto(5));

        var problem = result as ObjectResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [TestMethod]
    public async Task SelectResult_HardcoverDailyLimitExceeded_Returns400()
    {
        _service.Setup(s => s.SelectResultAsync(8, 0))
            .ThrowsAsync(new AudiobookManager.Scraping.RateLimiting.HardcoverDailyLimitExceededException(5000));

        var result = await _controller.SelectResult(8, new SelectOnlineMatchResultDto(0));

        var problem = result as ObjectResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    #endregion

    #region Reject / Dismiss

    [TestMethod]
    public async Task Reject_CallsServiceAndReturnsOk()
    {
        _service.Setup(s => s.RejectAsync(9)).ReturnsAsync(true);

        var result = await _controller.Reject(9);

        Assert.IsInstanceOfType<OkResult>(result);
        _service.Verify(s => s.RejectAsync(9), Times.Once);
    }

    [TestMethod]
    public async Task Dismiss_CallsServiceAndReturnsOk()
    {
        _service.Setup(s => s.DismissAsync(10)).ReturnsAsync(true);

        var result = await _controller.Dismiss(10);

        Assert.IsInstanceOfType<OkResult>(result);
        _service.Verify(s => s.DismissAsync(10), Times.Once);
    }

    // Dismissing an already-gone row is a no-op success, not a 404 - the controller does not even
    // look at the returned bool.
    [TestMethod]
    public async Task Dismiss_NoRowForThisBook_StillReturnsOk()
    {
        _service.Setup(s => s.DismissAsync(11)).ReturnsAsync(false);

        var result = await _controller.Dismiss(11);

        Assert.IsInstanceOfType<OkResult>(result);
    }

    #endregion

    #region GetPending / GetFailed paging validation

    [TestMethod]
    public async Task GetPending_NegativePage_IsA400()
    {
        var result = await _controller.GetPending(page: -1);

        ProblemAssert.HasDetail(result.Result, StatusCodes.Status400BadRequest, "page must be zero or greater.");
    }

    [TestMethod]
    public async Task GetPending_PageSizeZero_IsA400()
    {
        var result = await _controller.GetPending(page: 0, pageSize: 0);

        var problem = result.Result as ObjectResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [TestMethod]
    public async Task GetPending_PageSizeOverMax_IsA400()
    {
        var result = await _controller.GetPending(page: 0, pageSize: 10_000);

        var problem = result.Result as ObjectResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [TestMethod]
    public async Task GetPending_OffsetBeyondMax_IsA400()
    {
        var result = await _controller.GetPending(page: int.MaxValue, pageSize: 200);

        var problem = result.Result as ObjectResult;
        Assert.IsNotNull(problem);
        Assert.AreEqual(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [TestMethod]
    public async Task GetPending_Valid_QueriesTheServiceWithPendingStatusAndReturnsThePage()
    {
        var book = new Database.Models.Audiobook(
            1, "A Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/book.m4b", "book.m4b", 1000);
        var row = new PendingOnlineMatch
        {
            AudiobookId = 1,
            Audiobook = book,
            SearchedAt = DateTime.UtcNow,
            Status = PendingOnlineMatchStatus.Pending,
            SourceNamesJson = "[\"Audible\"]",
            ResultsJson = "{\"version\":1,\"results\":[]}",
        };
        _service.Setup(s => s.GetPageAsync(PendingOnlineMatchStatus.Pending, 0, 50))
            .ReturnsAsync((new List<PendingOnlineMatch> { row }, 1));

        var result = await _controller.GetPending();

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var dto = ok.Value as PendingOnlineMatchPageDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual(1, dto.Total);
        Assert.AreEqual(1, dto.Items.Count);
        Assert.AreEqual(1, dto.Items[0].AudiobookId);
    }

    [TestMethod]
    public async Task GetFailed_Valid_QueriesTheServiceWithRejectedStatus()
    {
        _service.Setup(s => s.GetPageAsync(PendingOnlineMatchStatus.Rejected, 0, 50))
            .ReturnsAsync((new List<PendingOnlineMatch>(), 0));

        var result = await _controller.GetFailed();

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        _service.Verify(s => s.GetPageAsync(PendingOnlineMatchStatus.Rejected, 0, 50), Times.Once);
    }

    #endregion
}
