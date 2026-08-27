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

    private static SimilarValueGroup MakeGroup() => new SimilarValueGroup
    {
        Candidates = new List<SimilarValueCandidate>
        {
            new SimilarValueCandidate
            {
                Value = "J.K. Rowling",
                Books = new List<SimilarValueBook> { new SimilarValueBook { Id = 1, BookName = "Book One" } }
            },
            new SimilarValueCandidate
            {
                Value = "JK Rowling",
                Books = new List<SimilarValueBook> { new SimilarValueBook { Id = 2, BookName = "Book Two" } }
            }
        }
    };

    [TestMethod]
    public async Task GetSimilarAuthors_ReturnsMappedDtoList()
    {
        _similarValueService.Setup(s => s.DetectSimilarAuthorsAsync()).ReturnsAsync(new List<SimilarValueGroup> { MakeGroup() });

        var result = await _controller.GetSimilarAuthors();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].Candidates.Count);
        Assert.AreEqual("J.K. Rowling", result[0].Candidates[0].Value);
        Assert.AreEqual(1, result[0].Candidates[0].Books[0].Id);
    }

    [TestMethod]
    public async Task GetSimilarSeries_ReturnsMappedDtoList()
    {
        _similarValueService.Setup(s => s.DetectSimilarSeriesAsync()).ReturnsAsync(new List<SimilarValueGroup> { MakeGroup() });

        var result = await _controller.GetSimilarSeries();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].Candidates.Count);
    }

    [TestMethod]
    public async Task GetAuthorNames_ReturnsDistinctSortedNames()
    {
        _personRepository.Setup(r => r.GetAllAuthorsAsync()).ReturnsAsync(new List<DbPerson>
        {
            new DbPerson(1, "Zed Author"),
            new DbPerson(2, "Amy Author"),
            new DbPerson(3, "Amy Author")
        });

        var result = await _controller.GetAuthorNames();

        CollectionAssert.AreEqual(new List<string> { "Amy Author", "Zed Author" }, result);
    }

    [TestMethod]
    public async Task GetSeriesNames_ReturnsSortedKeys()
    {
        _audiobookRepository.Setup(r => r.GetDistinctSeriesAsync()).ReturnsAsync(
            new Dictionary<string, List<(long Id, string BookName)>>
            {
                ["Zeta Series"] = new List<(long, string)> { (1, "Book") },
                ["Alpha Series"] = new List<(long, string)> { (2, "Book") }
            });

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

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
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

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
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

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
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
