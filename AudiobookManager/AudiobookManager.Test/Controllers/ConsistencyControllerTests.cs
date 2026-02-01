using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class ConsistencyControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IConsistencyIssueRepository> _issueRepository = null!;
    private Mock<ILogger<ConsistencyController>> _logger = null!;
    private ConsistencyController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _logger = new Mock<ILogger<ConsistencyController>>();

        _controller = new ConsistencyController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _issueRepository.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task GetIssues_ReturnsMapDtoList()
    {
        var dbAudiobook = new Database.Models.Audiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
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

        Assert.IsInstanceOfType(result, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task ResolveIssue_Success_ReturnsOk()
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

        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService)))
            .Returns(mockConsistencyService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var result = await _controller.ResolveIssue(1);

        Assert.IsInstanceOfType(result, typeof(OkResult));
        mockConsistencyService.Verify(s => s.ResolveIssue(1), Times.Once);
    }
}
