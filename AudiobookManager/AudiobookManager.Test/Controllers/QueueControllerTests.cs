using AudiobookManager.Api.Controllers;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class QueueControllerTests
{
    private Mock<IQueuedOrganizeTaskService> _organizeTaskService = null!;
    private QueueController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _organizeTaskService = new Mock<IQueuedOrganizeTaskService>();
        _controller = new QueueController(_organizeTaskService.Object);
    }

    private static Audiobook MakeAudiobook(string bookName = "Book") =>
        new Audiobook(
            new List<Person> { new Person("Author") },
            bookName,
            2020,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000));

    [TestMethod]
    public async Task Index_ReturnsOriginalFileLocationsOfQueuedTasks()
    {
        var tasks = new List<QueuedOrganizeTask>
        {
            new QueuedOrganizeTask("/import/one.m4b", MakeAudiobook("One"), DateTime.UtcNow),
            new QueuedOrganizeTask("/import/two.m4b", MakeAudiobook("Two"), DateTime.UtcNow)
        };

        _organizeTaskService.Setup(s => s.GetQueuedOrganizeTasks()).ReturnsAsync(tasks);

        var result = await _controller.Index();

        CollectionAssert.AreEqual(new List<string> { "/import/one.m4b", "/import/two.m4b" }, (System.Collections.ICollection)result);
    }

    [TestMethod]
    public async Task Index_NoQueuedTasks_ReturnsEmptyList()
    {
        _organizeTaskService.Setup(s => s.GetQueuedOrganizeTasks()).ReturnsAsync(new List<QueuedOrganizeTask>());

        var result = await _controller.Index();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetFailed_MapsRepositoryRowsToDtos()
    {
        var failedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var queuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _organizeTaskService.Setup(s => s.GetFailedQueuedOrganizeTasks())
            .ReturnsAsync(new List<FailedOrganizeTaskRow>
            {
                new("/import/bad.m4b", queuedAt, 3, "not valid json", failedAt),
            });

        var result = await _controller.GetFailed();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("/import/bad.m4b", result[0].OriginalFileLocation);
        Assert.AreEqual(queuedAt, result[0].QueuedTime);
        Assert.AreEqual(3, result[0].FailureCount);
        Assert.AreEqual("not valid json", result[0].LastFailureReason);
        Assert.AreEqual(failedAt, result[0].LastFailureAt);
    }

    [TestMethod]
    public async Task GetFailed_NoFailedTasks_ReturnsEmptyList()
    {
        _organizeTaskService.Setup(s => s.GetFailedQueuedOrganizeTasks()).ReturnsAsync(new List<FailedOrganizeTaskRow>());

        var result = await _controller.GetFailed();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task DeleteFailed_CallsDeleteWithTheGivenPath()
    {
        var result = await _controller.DeleteFailed("/import/bad.m4b");

        _organizeTaskService.Verify(s => s.DeleteQueuedOrganizeTask("/import/bad.m4b"), Times.Once);
        Assert.IsInstanceOfType<NoContentResult>(result);
    }

    [TestMethod]
    public async Task RetryFailed_RowExists_ReturnsNoContent()
    {
        _organizeTaskService.Setup(s => s.RetryQueuedOrganizeTask("/import/bad.m4b")).ReturnsAsync(true);

        var result = await _controller.RetryFailed("/import/bad.m4b");

        Assert.IsInstanceOfType<NoContentResult>(result);
    }

    [TestMethod]
    public async Task RetryFailed_NoSuchRow_ReturnsNotFound()
    {
        _organizeTaskService.Setup(s => s.RetryQueuedOrganizeTask("/import/gone.m4b")).ReturnsAsync(false);

        var result = await _controller.RetryFailed("/import/gone.m4b");

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }
}
