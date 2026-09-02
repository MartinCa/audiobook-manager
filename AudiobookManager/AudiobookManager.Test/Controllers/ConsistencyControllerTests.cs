using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class ConsistencyControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<IConsistencyIssueRepository> _issueRepository = null!;
    private Mock<IOrphanDirectoryRepository> _orphanDirectoryRepository = null!;
    private Mock<ILogger<ConsistencyController>> _logger = null!;
    private ConsistencyController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _orphanDirectoryRepository = new Mock<IOrphanDirectoryRepository>();
        _logger = new Mock<ILogger<ConsistencyController>>();

        _controller = new ConsistencyController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            _issueRepository.Object,
            _orphanDirectoryRepository.Object,
            Mock.Of<IHostApplicationLifetime>(),
            _logger.Object);
    }

    [TestMethod]
    public async Task GetIssues_ReturnsMapDtoList()
    {
        var dbAudiobook = new Database.Models.Audiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/path/test.m4b", "test.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") }
        };

        var issues = new List<ConsistencyIssue>
        {
            new ConsistencyIssue
            {
                Id = 1,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.MissingDescTxt,
                Description = "desc.txt missing",
                ExpectedValue = "Some description",
                ActualValue = null,
                DetectedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        _issueRepository.Setup(r => r.GetAllWithAudiobookAsync()).ReturnsAsync(issues);

        var result = await _controller.GetIssues();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].Id);
        Assert.AreEqual(1, result[0].AudiobookId);
        Assert.AreEqual("Test Book", result[0].BookName);
        Assert.AreEqual("Author One", result[0].Authors[0]);
        Assert.AreEqual("MissingDescTxt", result[0].IssueType);
        Assert.AreEqual("desc.txt missing", result[0].Description);
        Assert.AreEqual("Some description", result[0].ExpectedValue);
        Assert.IsNull(result[0].ActualValue);
    }

    [TestMethod]
    public async Task ResolveIssue_NotFound_Returns404()
    {
        _issueRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ConsistencyIssue?)null);

        var result = await _controller.ResolveIssue(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    // A resolve rewrites the book's files, so it takes the same per-audiobook gate a save does.
    // Losing that race is "try again", not a 500 - which is what the catch-all produced.
    [TestMethod]
    public async Task ResolveIssue_BookIsBeingModifiedElsewhere_ReturnsConflict()
    {
        var issue = new ConsistencyIssue
        {
            Id = 1,
            AudiobookId = 7,
            IssueType = ConsistencyIssueType.MissingDescTxt,
            Description = "test",
            DetectedAt = DateTime.UtcNow
        };

        _issueRepository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(issue);

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.ResolveIssue(1)).ThrowsAsync(new AudiobookBusyException(7));

        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.ResolveIssue(1);

        Assert.IsInstanceOfType(result.Result, typeof(ConflictObjectResult));
    }

    [TestMethod]
    public async Task ResolveIssue_Success_ReturnsOkWithResultDto()
    {
        var issue = new ConsistencyIssue
        {
            Id = 1,
            AudiobookId = 1,
            IssueType = ConsistencyIssueType.MissingDescTxt,
            Description = "test",
            DetectedAt = DateTime.UtcNow
        };

        _issueRepository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(issue);

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.ResolveIssue(1))
            .ReturnsAsync(new ConsistencyResolveResult(1, ConsistencyIssueType.MissingDescTxt, "resolved", "Metadata sidecar files updated."));

        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.ResolveIssue(1);

        Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
        var okResult = (OkObjectResult)result.Result!;
        var dto = (ConsistencyResolveResultDto)okResult.Value!;
        Assert.AreEqual(1, dto.IssueId);
        Assert.AreEqual("resolved", dto.ActionTaken);
        mockConsistencyService.Verify(s => s.ResolveIssue(1), Times.Once);
    }

    [TestMethod]
    public async Task RecheckAudiobook_Success_ReturnsReloadedIssues()
    {
        var dbAudiobook = new Database.Models.Audiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/path/test.m4b", "test.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") }
        };

        var reloadedIssues = new List<ConsistencyIssue>
        {
            new ConsistencyIssue
            {
                Id = 5,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.WrongFilePath,
                Description = "File path does not match expected path from tags",
                ExpectedValue = "/library/expected.m4b",
                ActualValue = "/path/test.m4b",
                DetectedAt = DateTime.UtcNow
            }
        };

        _issueRepository.Setup(r => r.GetByAudiobookIdAsync(1)).ReturnsAsync(reloadedIssues);

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();

        mockConsistencyService.Setup(s => s.RecheckAudiobookAsync(1)).ReturnsAsync(reloadedIssues);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.RecheckAudiobook(1) as OkObjectResult;

        Assert.IsNotNull(result);
        var dtos = result.Value as List<AudiobookManager.Api.Dtos.ConsistencyIssueDto>;
        Assert.IsNotNull(dtos);
        Assert.AreEqual(1, dtos.Count);
        Assert.AreEqual(5, dtos[0].Id);
        Assert.AreEqual("Test Book", dtos[0].BookName);
        Assert.AreEqual("WrongFilePath", dtos[0].IssueType);
        mockConsistencyService.Verify(s => s.RecheckAudiobookAsync(1), Times.Once);
    }

    [TestMethod]
    public async Task RecheckAudiobook_NotFound_Returns404()
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();

        mockConsistencyService.Setup(s => s.RecheckAudiobookAsync(999))
            .ThrowsAsync(new KeyNotFoundException());
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.RecheckAudiobook(999);

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task GetOrphanDirectories_ReturnsMappedDtoList()
    {
        var directories = new List<OrphanDirectory>
        {
            new OrphanDirectory
            {
                Id = 1,
                DirectoryPath = "/library/Author/Book",
                DetectedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        _orphanDirectoryRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(directories);

        var result = await _controller.GetOrphanDirectories();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].Id);
        Assert.AreEqual("/library/Author/Book", result[0].DirectoryPath);
    }

    [TestMethod]
    public async Task ResolveOrphanDirectory_Success_ReturnsOk()
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();

        mockConsistencyService.Setup(s => s.ResolveOrphanDirectory(1))
            .ReturnsAsync(new OrphanDirectoryResolveResult(1, "/path", "deleted", "Orphan directory deleted from disk."));
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.ResolveOrphanDirectory(1);

        Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
        var okResult = (OkObjectResult)result.Result!;
        var dto = (OrphanDirectoryResolveResultDto)okResult.Value!;
        Assert.AreEqual("deleted", dto.ActionTaken);
        mockConsistencyService.Verify(s => s.ResolveOrphanDirectory(1), Times.Once);
    }

    [TestMethod]
    public async Task ResolveOrphanDirectory_NotFound_Returns404()
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();

        mockConsistencyService.Setup(s => s.ResolveOrphanDirectory(999))
            .ThrowsAsync(new KeyNotFoundException());
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.ResolveOrphanDirectory(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    private void SetupScope(ILibraryConsistencyService service)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(service);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
    }

    [TestMethod]
    public async Task GetTagMismatchFields_Success_ReturnsOkWithFieldDtos()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.GetTagMismatchFieldsAsync(5))
            .ReturnsAsync(new List<TagMismatchField>
            {
                new("Book Name", "Library Title", "File Title"),
                new("Year", "2010", "2020")
            });
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.GetTagMismatchFields(5);

        Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
        var okResult = (OkObjectResult)result.Result!;
        var dtos = (List<TagMismatchFieldDto>)okResult.Value!;
        Assert.AreEqual(2, dtos.Count);
        Assert.AreEqual("Book Name", dtos[0].Field);
        Assert.AreEqual("Library Title", dtos[0].LibraryValue);
        Assert.AreEqual("File Title", dtos[0].FileValue);
        mockConsistencyService.Verify(s => s.GetTagMismatchFieldsAsync(5), Times.Once);
    }

    [TestMethod]
    public async Task GetTagMismatchFields_NotFound_Returns404()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.GetTagMismatchFieldsAsync(999))
            .ThrowsAsync(new KeyNotFoundException());
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.GetTagMismatchFields(999);

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task GetTagMismatchFields_WrongIssueType_ReturnsBadRequest()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.GetTagMismatchFieldsAsync(5))
            .ThrowsAsync(new ArgumentException("Issue 5 is not a TagMismatch"));
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.GetTagMismatchFields(5);

        Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        var badRequest = (BadRequestObjectResult)result.Result!;
        StringAssert.Contains(badRequest.Value!.ToString()!, "not a TagMismatch");
    }

    [TestMethod]
    public async Task ResolveTagMismatch_Success_ReturnsOkWithResultDto()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.ResolveTagMismatchAsync(5, It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .ReturnsAsync(new ConsistencyResolveResult(5, ConsistencyIssueType.TagMismatch, "resolved", "Selected tag values applied and file path updated."));
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.ResolveTagMismatch(5, new ResolveTagMismatchRequest
        {
            FieldValues = new Dictionary<string, string?> { ["Book Name"] = "Chosen Title" }
        });

        Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
        var okResult = (OkObjectResult)result.Result!;
        var dto = (ConsistencyResolveResultDto)okResult.Value!;
        Assert.AreEqual(5, dto.IssueId);
        Assert.AreEqual("resolved", dto.ActionTaken);
        mockConsistencyService.Verify(
            s => s.ResolveTagMismatchAsync(5, It.Is<IReadOnlyDictionary<string, string?>>(d =>
                d["Book Name"] == "Chosen Title")),
            Times.Once);
    }

    [TestMethod]
    public async Task ResolveTagMismatch_NotFound_Returns404()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.ResolveTagMismatchAsync(999, It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .ThrowsAsync(new KeyNotFoundException());
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.ResolveTagMismatch(999, new ResolveTagMismatchRequest());

        Assert.IsInstanceOfType(result.Result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task ResolveTagMismatch_BookBusy_ReturnsConflict()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.ResolveTagMismatchAsync(5, It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .ThrowsAsync(new AudiobookBusyException(7));
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.ResolveTagMismatch(5, new ResolveTagMismatchRequest());

        Assert.IsInstanceOfType(result.Result, typeof(ConflictObjectResult));
    }

    [TestMethod]
    public async Task ResolveTagMismatch_StructuralFieldCleared_ReturnsBadRequest()
    {
        var mockConsistencyService = new Mock<ILibraryConsistencyService>();
        mockConsistencyService.Setup(s => s.ResolveTagMismatchAsync(5, It.IsAny<IReadOnlyDictionary<string, string?>>()))
            .ThrowsAsync(new ArgumentException("Field 'Year' cannot be cleared: it determines the library path"));
        SetupScope(mockConsistencyService.Object);

        var result = await _controller.ResolveTagMismatch(5, new ResolveTagMismatchRequest());

        Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        var badRequest = (BadRequestObjectResult)result.Result!;
        StringAssert.Contains(badRequest.Value!.ToString()!, "cannot be cleared");
    }
}
