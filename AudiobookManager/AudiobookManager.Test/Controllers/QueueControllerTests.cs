using AudiobookManager.Api.Controllers;
using AudiobookManager.Domain;
using AudiobookManager.Services;
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
}
