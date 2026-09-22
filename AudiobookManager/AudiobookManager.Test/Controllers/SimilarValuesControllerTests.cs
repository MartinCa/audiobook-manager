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
    public async Task GetEntryStatus_Author_ReturnsClassifiedStatus()
    {
        _similarValueService
            .Setup(s => s.GetEntryStatusAsync(Domain.EntryValueKind.Author, "Jane Authorr", 3))
            .ReturnsAsync(new EntryValueStatus(
                "Jane Authorr",
                Domain.EntryValueStatusKind.Similar,
                ExactMatch: null,
                new List<EntryValueMatch> { new(1, "Jane Author") }));

        var result = await _controller.GetEntryStatus("author", "Jane Authorr");

        var dto = result.Value as EntryStatusDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual("similar", dto.Status);
        Assert.IsNull(dto.ExactMatch);
        Assert.AreEqual(1, dto.SimilarMatches.Count);
        Assert.AreEqual(1, dto.SimilarMatches[0].Id);
        Assert.AreEqual("Jane Author", dto.SimilarMatches[0].Name);
    }

    [TestMethod]
    public async Task GetEntryStatus_Series_ReturnsExactStatus()
    {
        _similarValueService
            .Setup(s => s.GetEntryStatusAsync(Domain.EntryValueKind.Series, "Mistborn", 3))
            .ReturnsAsync(new EntryValueStatus(
                "Mistborn",
                Domain.EntryValueStatusKind.Exact,
                new EntryValueMatch(null, "Mistborn"),
                new List<EntryValueMatch>()));

        var result = await _controller.GetEntryStatus("series", "Mistborn");

        var dto = result.Value as EntryStatusDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual("exact", dto.Status);
        Assert.IsNotNull(dto.ExactMatch);
        Assert.IsNull(dto.ExactMatch.Id, "series matches carry no id");
        Assert.AreEqual("Mistborn", dto.ExactMatch.Name);
    }

    [TestMethod]
    public async Task GetEntryStatus_Narrator_ReturnsClassifiedStatus()
    {
        _similarValueService
            .Setup(s => s.GetEntryStatusAsync(Domain.EntryValueKind.Narrator, "Michael Kramer", 3))
            .ReturnsAsync(new EntryValueStatus(
                "Michael Kramer",
                Domain.EntryValueStatusKind.Exact,
                new EntryValueMatch(2, "Michael Kramer"),
                new List<EntryValueMatch>()));

        var result = await _controller.GetEntryStatus("narrator", "Michael Kramer");

        var dto = result.Value as EntryStatusDto;
        Assert.IsNotNull(dto);
        Assert.AreEqual("exact", dto.Status);
        Assert.AreEqual(2, dto.ExactMatch?.Id);
    }

    [TestMethod]
    [DataRow("invalid", "x")]
    [DataRow("author", "")]
    [DataRow("author", "   ")]
    public async Task GetEntryStatus_InvalidInput_IsRefused(string valueType, string value)
    {
        var result = await _controller.GetEntryStatus(valueType, value);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _similarValueService.Verify(
            s => s.GetEntryStatusAsync(It.IsAny<Domain.EntryValueKind>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(11)]
    public async Task GetEntryStatus_OutOfRangeLimit_IsRefused(int limit)
    {
        var result = await _controller.GetEntryStatus("author", "x", limit);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
    }

    [TestMethod]
    public async Task GetAutocomplete_Author_ReturnsBoundedNameList()
    {
        _personRepository.Setup(r => r.SearchAuthorNamesAsync("Brand", 8))
            .ReturnsAsync(new List<AuthorSummaryRow>
            {
                new(1, "Brandon Sanderson", 2),
                new(2, "Brandon", 1),
            });

        var result = await _controller.GetAutocomplete("author", "Brand", 8);

        var names = result.Value as List<string>;
        Assert.IsNotNull(names);
        CollectionAssert.AreEqual(new List<string> { "Brandon Sanderson", "Brandon" }, names);
    }

    [TestMethod]
    public async Task GetAutocomplete_Series_DelegatesToSeriesPrefilter()
    {
        _audiobookRepository.Setup(a => a.SearchSeriesValuesAsync("Mist", 5))
            .ReturnsAsync(new List<string> { "Mistborn" });

        var result = await _controller.GetAutocomplete("series", "Mist", 5);

        var names = result.Value as List<string>;
        Assert.IsNotNull(names);
        CollectionAssert.AreEqual(new List<string> { "Mistborn" }, names);
    }

    [TestMethod]
    public async Task GetAutocomplete_Narrator_DelegatesToNarratorPrefilter()
    {
        _personRepository.Setup(r => r.SearchNarratorNamesAsync("Micha", 5))
            .ReturnsAsync(new List<AuthorSummaryRow>
            {
                new(2, "Michael Kramer", 0),
                new(3, "Michael Page", 0),
            });

        var result = await _controller.GetAutocomplete("narrator", "Micha", 5);

        var names = result.Value as List<string>;
        Assert.IsNotNull(names);
        CollectionAssert.AreEqual(new List<string> { "Michael Kramer", "Michael Page" }, names);
    }

    [TestMethod]
    [DataRow("invalid", "x")]
    [DataRow("author", "")]
    [DataRow("author", "   ")]
    public async Task GetAutocomplete_InvalidInput_IsRefused(string valueType, string query)
    {
        var result = await _controller.GetAutocomplete(valueType, query);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
    }

    [TestMethod]
    public async Task GetIgnoredPairs_Author_ReturnsMappedDtos()
    {
        var ignoredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _similarValueService.Setup(s => s.GetIgnoredPairsAsync("authors"))
            .ReturnsAsync(new List<IgnoredSimilarValuePairInfo>
            {
                new(5, "Ben Winters", "Ed Winters", ignoredAt),
            });

        var result = await _controller.GetIgnoredPairs("author");

        var dtos = result.Value;
        Assert.IsNotNull(dtos);
        Assert.AreEqual(1, dtos!.Count);
        Assert.AreEqual(5, dtos[0].Id);
        Assert.AreEqual("Ben Winters", dtos[0].ValueA);
        Assert.AreEqual("Ed Winters", dtos[0].ValueB);
        Assert.AreEqual(ignoredAt, dtos[0].IgnoredAtUtc);
    }

    [TestMethod]
    public async Task GetIgnoredPairs_InvalidValueType_ReturnsBadRequest()
    {
        var result = await _controller.GetIgnoredPairs("narrator");

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result.Result!).StatusCode);
        _similarValueService.Verify(s => s.GetIgnoredPairsAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task IgnorePair_ValidRequest_CallsServiceWithMappedKind()
    {
        _similarValueService
            .Setup(s => s.IgnorePairAsync("series", "The Mistborn Saga", It.IsAny<List<string>>()))
            .ReturnsAsync(true);

        var result = await _controller.IgnorePair(new IgnoreSimilarValuePairDto
        {
            ValueType = "series",
            Value = "The Mistborn Saga",
            AgainstValues = new List<string> { "Mistborn Saga" },
        });

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _similarValueService.Verify(
            s => s.IgnorePairAsync("series", "The Mistborn Saga", It.Is<List<string>>(l => l.Contains("Mistborn Saga"))),
            Times.Once);
    }

    // Regression: the service rejects a value/againstValues set that no longer exists in the
    // library (e.g. a stale client tab holding a group an alignment has since folded away) by
    // returning false - the controller must surface that as InvalidRequest, not a bare Ok().
    [TestMethod]
    public async Task IgnorePair_ServiceRejectsStaleValues_ReturnsBadRequest()
    {
        _similarValueService
            .Setup(s => s.IgnorePairAsync("author", "Ben Winters", It.IsAny<List<string>>()))
            .ReturnsAsync(false);

        var result = await _controller.IgnorePair(new IgnoreSimilarValuePairDto
        {
            ValueType = "author",
            Value = "Ben Winters",
            AgainstValues = new List<string> { "Ed Winters" },
        });

        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    [DataRow("invalid", "value", true)]
    [DataRow("author", "", true)]
    [DataRow("author", "value", false)]
    public async Task IgnorePair_InvalidRequest_ReturnsBadRequest(string valueType, string value, bool hasAgainstValues)
    {
        var result = await _controller.IgnorePair(new IgnoreSimilarValuePairDto
        {
            ValueType = valueType,
            Value = value,
            AgainstValues = hasAgainstValues ? new List<string> { "Other" } : new List<string>(),
        });

        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result).StatusCode);
        _similarValueService.Verify(
            s => s.IgnorePairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
    }

    // Regression: a blank AgainstValues entry used to sail past validation and hit the
    // [Required]/NOT-NULL entity columns downstream, surfacing as a raw 500 instead of the
    // InvalidRequest 400 every other malformed-body case on this endpoint gets.
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task IgnorePair_BlankAgainstValuesEntry_ReturnsBadRequest(string blankEntry)
    {
        var result = await _controller.IgnorePair(new IgnoreSimilarValuePairDto
        {
            ValueType = "author",
            Value = "Ben Winters",
            AgainstValues = new List<string> { "Ed Winters", blankEntry },
        });

        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result).StatusCode);
        _similarValueService.Verify(
            s => s.IgnorePairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
    }

    [TestMethod]
    public async Task RemoveIgnoredPair_ValidRequest_CallsServiceWithMappedKind()
    {
        var result = await _controller.RemoveIgnoredPair(5, "author");

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _similarValueService.Verify(s => s.RemoveIgnoredPairAsync("authors", 5), Times.Once);
    }

    [TestMethod]
    public async Task RemoveIgnoredPair_InvalidValueType_ReturnsBadRequest()
    {
        var result = await _controller.RemoveIgnoredPair(5, "narrator");

        Assert.IsInstanceOfType(result, typeof(ObjectResult));
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result).StatusCode);
        _similarValueService.Verify(s => s.RemoveIgnoredPairAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
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
