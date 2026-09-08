using Microsoft.AspNetCore.Http;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using DbPerson = AudiobookManager.Database.Models.Person;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SimilarValuesControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IOrganize> _clientProxy = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<ISimilarValueService> _similarValueService = null!;
    private Mock<IPersonRepository> _personRepository = null!;
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<ILogger<SimilarValuesController>> _logger = null!;
    private SimilarValuesController _controller = null!;

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
        _similarValueService = new Mock<ISimilarValueService>();
        _personRepository = new Mock<IPersonRepository>();
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _logger = new Mock<ILogger<SimilarValuesController>>();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ISimilarValueService))).Returns(_similarValueService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _controller = new SimilarValuesController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            _similarValueService.Object,
            _personRepository.Object,
            _audiobookRepository.Object,
            Mock.Of<IHostApplicationLifetime>(),
            _logger.Object);
    }

    // BackgroundOperationRunner calls statusRegistry.SetFinished(key) and THEN releases the
    // static gate in its finally block, so waiting for SetFinished alone can race the gate
    // release (Moq callbacks/TaskCompletionSource can resume our continuation synchronously,
    // inline with the SetFinished call, before the runner's very next statement executes).
    // RunContinuationsAsynchronously keeps that resumption off the runner's thread;
    // AwaitOperationFinished then waits on the gate itself.
    private Task RegisterFinishedWaiter()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry.Setup(r => r.SetFinished(SimilarValuesController.OperationKey)).Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private static async Task AwaitOperationFinished(Task finishedSignal)
    {
        await finishedSignal.WaitAsync(TimeSpan.FromSeconds(5));
        await OperationGate.WaitUntilReleasedAsync(typeof(SimilarValuesController));
    }

    private static SimilarValueGroup MakeGroup(string value = "J.K. Rowling", int bookCount = 12) => new SimilarValueGroup
    {
        Candidates = new List<SimilarValueCandidate>
        {
            new SimilarValueCandidate { Value = value, BookCount = bookCount },
            new SimilarValueCandidate { Value = $"{value} (alt)".Replace(" (alt)", "2"), BookCount = bookCount - 1 }
        }
    };

    [TestMethod]
    public async Task GetSimilarAuthors_ReturnsOnePageWithItemsAndTotal()
    {
        _similarValueService
            .Setup(s => s.DetectSimilarAuthorsAsync(skip: 0, take: 50))
            .ReturnsAsync((new List<SimilarValueGroup> { MakeGroup("J.K. Rowling", 12) }, 7));

        var result = await _controller.GetSimilarAuthors();

        var page = ((OkObjectResult)result.Result!).Value as SimilarValueGroupsPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(7, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(2, page.Items[0].Candidates.Count);
        Assert.AreEqual("J.K. Rowling", page.Items[0].Candidates[0].Value);
        Assert.AreEqual(12, page.Items[0].Candidates[0].BookCount, "Candidates carry a book count, not a book list.");
    }

    [TestMethod]
    public async Task GetSimilarAuthors_PassesPageAndPageSizeThrough()
    {
        _similarValueService
            .Setup(s => s.DetectSimilarAuthorsAsync(skip: 100, take: 25))
            .ReturnsAsync((new List<SimilarValueGroup>(), 4));

        var result = await _controller.GetSimilarAuthors(page: 4, pageSize: 25);

        var page = ((OkObjectResult)result.Result!).Value as SimilarValueGroupsPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(4, page.TotalCount);
        _similarValueService.Verify(s => s.DetectSimilarAuthorsAsync(skip: 100, take: 25), Times.Once);
    }

    [TestMethod]
    [DataRow(-1, 50)]
    [DataRow(0, 0)]
    [DataRow(0, 201)]
    public async Task GetSimilarAuthors_AnOutOfRangePage_IsRefused(int page, int pageSize)
    {
        var result = await _controller.GetSimilarAuthors(page: page, pageSize: pageSize);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _similarValueService.Verify(
            s => s.DetectSimilarAuthorsAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    // Regression: the offset wraps negative on a huge page and SQLite reads a negative OFFSET as
    // zero - silently serving the first page. Same guard as every other paged endpoint here.
    [TestMethod]
    public async Task GetSimilarAuthors_APageLargeEnoughToOverflowTheOffset_IsRefused()
    {
        var result = await _controller.GetSimilarAuthors(page: 11_000_000, pageSize: 200);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _similarValueService.Verify(
            s => s.DetectSimilarAuthorsAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetSimilarSeries_ReturnsOnePageWithItemsAndTotal()
    {
        _similarValueService
            .Setup(s => s.DetectSimilarSeriesAsync(skip: 0, take: 50))
            .ReturnsAsync((new List<SimilarValueGroup> { MakeGroup("Mistborn", 3) }, 2));

        var result = await _controller.GetSimilarSeries();

        var page = ((OkObjectResult)result.Result!).Value as SimilarValueGroupsPageDto;
        Assert.IsNotNull(page);
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(2, page.Items[0].Candidates.Count);
    }

    [TestMethod]
    public async Task GetAuthorNames_ReturnsDistinctSortedNames()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync())
            .ReturnsAsync(new List<string> { "Amy Author", "Zed Author" });

        var result = await _controller.GetAuthorNames();

        CollectionAssert.AreEqual(new List<string> { "Amy Author", "Zed Author" }, result);
    }

    [TestMethod]
    public async Task GetNarratorNames_ReturnsDistinctSortedNames()
    {
        _personRepository.Setup(r => r.GetNarratorNamesAsync())
            .ReturnsAsync(new List<string> { "Amy Narrator", "Zed Narrator" });

        var result = await _controller.GetNarratorNames();

        CollectionAssert.AreEqual(new List<string> { "Amy Narrator", "Zed Narrator" }, result);
    }

    [TestMethod]
    public async Task GetSeriesNames_ReturnsSortedKeys()
    {
        _audiobookRepository.Setup(r => r.GetSeriesNamesAsync())
            .ReturnsAsync(new List<string> { "Alpha Series", "Zeta Series" });

        var result = await _controller.GetSeriesNames();

        CollectionAssert.AreEqual(new List<string> { "Alpha Series", "Zeta Series" }, result);
    }

    [TestMethod]
    public void StartAlign_InvalidValueType_ReturnsBadRequest()
    {
        var result = _controller.StartAlign(new AlignSimilarValuesDto
        {
            ValueType = "invalid",
            SourceValues = new List<string> { "A" },
            TargetValue = "B"
        });

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public void StartAlign_EmptySourceValues_ReturnsBadRequest()
    {
        var result = _controller.StartAlign(new AlignSimilarValuesDto
        {
            ValueType = "author",
            SourceValues = new List<string>(),
            TargetValue = "B"
        });

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public void StartAlign_BlankTargetValue_ReturnsBadRequest()
    {
        var result = _controller.StartAlign(new AlignSimilarValuesDto
        {
            ValueType = "author",
            SourceValues = new List<string> { "A" },
            TargetValue = "  "
        });

        ProblemAssert.HasStatus(result, StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task StartAlign_Authors_ReturnsOkImmediately_AndWiresProgressAndCompletion()
    {
        _similarValueService.Setup(s => s.AlignAuthorsAsync(
                It.Is<List<string>>(l => l.Contains("J.K. Rowling")), "JK Rowling", It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((List<string> _, string __, Func<int, int, int, int, Task> progressAction) =>
            {
                progressAction(1, 1, 1, 0).GetAwaiter().GetResult();
                return (1, 1, 0);
            });

        var finished = RegisterFinishedWaiter();

        var result = _controller.StartAlign(new AlignSimilarValuesDto
        {
            ValueType = "author",
            SourceValues = new List<string> { "J.K. Rowling" },
            TargetValue = "JK Rowling"
        });

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _clientProxy.Verify(c => c.SimilarValueAlignProgress(It.Is<SimilarValueAlignProgress>(p => p.Processed == 1)), Times.Once);
        _clientProxy.Verify(c => c.SimilarValueAlignComplete(It.Is<SimilarValueAlignComplete>(p => p.TotalSucceeded == 1)), Times.Once);
        _similarValueService.Verify(s => s.AlignSeriesAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<Func<int, int, int, int, Task>>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAlign_Series_CallsAlignSeriesNotAlignAuthors()
    {
        _similarValueService.Setup(s => s.AlignSeriesAsync(
                It.IsAny<List<string>>(), "Target Series", It.IsAny<Func<int, int, int, int, Task>>()))
            .ReturnsAsync((2, 2, 0));

        var finished = RegisterFinishedWaiter();

        var result = _controller.StartAlign(new AlignSimilarValuesDto
        {
            ValueType = "series",
            SourceValues = new List<string> { "Series A", "Series B" },
            TargetValue = "Target Series"
        });

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await AwaitOperationFinished(finished);

        _similarValueService.Verify(s => s.AlignAuthorsAsync(It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<Func<int, int, int, int, Task>>()), Times.Never);
        _clientProxy.Verify(c => c.SimilarValueAlignComplete(It.Is<SimilarValueAlignComplete>(p => p.TotalSucceeded == 2)), Times.Once);
    }
}
