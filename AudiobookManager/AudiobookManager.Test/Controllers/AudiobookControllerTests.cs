using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class AudiobookControllerTests
{
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<IQueuedOrganizeTaskService> _organizeTaskService = null!;
    private Mock<ILibraryConsistencyService> _libraryConsistencyService = null!;
    private Mock<ILogger<AudiobookController>> _logger = null!;
    private AudiobookController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookService = new Mock<IAudiobookService>();
        _organizeTaskService = new Mock<IQueuedOrganizeTaskService>();
        _libraryConsistencyService = new Mock<ILibraryConsistencyService>();
        _logger = new Mock<ILogger<AudiobookController>>();

        _controller = new AudiobookController(
            _audiobookService.Object,
            _organizeTaskService.Object,
            _libraryConsistencyService.Object,
            _logger.Object);
    }

    private static OrganizeAudiobookDto MakeDto(string bookName = "Test Book", string? series = null, string? seriesPart = null) => new()
    {
        BookName = bookName,
        Series = series,
        SeriesPart = seriesPart,
        Year = 2024,
        Authors = new List<string> { "Test Author" },
        Narrators = new List<string> { "Test Narrator" },
        FilePath = "/import/test.m4b",
        FileName = "test.m4b",
        SizeInBytes = 1000
    };

    [TestMethod]
    public void ParseAudiobook_DelegatesToService()
    {
        var expected = new Audiobook(
            new List<Person> { new Person("Author") },
            "Parsed Book",
            2024,
            new AudiobookFileInfo("/path/book.m4b", "book.m4b", 1000));

        _audiobookService.Setup(s => s.ParseAudiobook("/path/book.m4b")).Returns(expected);

        var result = _controller.ParseAudiobook(new PathDto { Path = "/path/book.m4b" });

        Assert.AreEqual("Parsed Book", result.BookName);
        _audiobookService.Verify(s => s.ParseAudiobook("/path/book.m4b"), Times.Once);
    }

    [TestMethod]
    public async Task OrganizeAudiobook_QueuesTaskAndReturnsOriginalFileLocation()
    {
        var dto = MakeDto();
        var queuedTask = new QueuedOrganizeTask("/import/test.m4b", new Audiobook(new List<Person>(), "Test Book", 2024, new AudiobookFileInfo("/import/test.m4b", "test.m4b", 1000)), DateTime.UtcNow);

        _organizeTaskService.Setup(s => s.QueueOrganizeTask(It.IsAny<Audiobook>())).ReturnsAsync(queuedTask);

        var result = await _controller.OrganizeAudiobook(dto);

        Assert.AreEqual("/import/test.m4b", result);
        _organizeTaskService.Verify(s => s.QueueOrganizeTask(It.Is<Audiobook>(a =>
            a.BookName == "Test Book" &&
            a.Authors.Count == 1 &&
            a.Authors[0].Name == "Test Author")), Times.Once);
    }

    [TestMethod]
    public void GeneratePath_DelegatesToService()
    {
        var dto = MakeDto();

        _audiobookService.Setup(s => s.GenerateLibraryPath(It.IsAny<Audiobook>())).Returns("/library/Test Author/2024 - Test Book/test.m4b");

        var result = _controller.GeneratePath(dto);

        Assert.AreEqual("/library/Test Author/2024 - Test Book/test.m4b", result);
        _audiobookService.Verify(s => s.GenerateLibraryPath(It.Is<Audiobook>(a => a.BookName == "Test Book")), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_DelegatesToAudiobookServiceUpdateAudiobook()
    {
        // Regression guard for the CLAUDE.md binding invariant: Author/Series/SeriesPart/Year/BookName
        // edits must always flow through AudiobookService.UpdateAudiobook, never a raw repository call.
        var dto = MakeDto("Updated Book Name", series: "Some Series", seriesPart: "2");

        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Updated Book Name",
            2024,
            new AudiobookFileInfo("/library/Test Author/Some Series/Book 02 - 2024 - Updated Book Name/test.m4b", "test.m4b", 1000));

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>())).ReturnsAsync(updated);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1)).ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        var result = await _controller.UpdateAudiobook(1, dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));
        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.Is<Audiobook>(a =>
            a.BookName == "Updated Book Name" &&
            a.Series == "Some Series" &&
            a.SeriesPart == "2" &&
            a.Year == 2024)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_AlsoRechecksConsistencyAfterSave()
    {
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/Test Author/2024 - Test Book/test.m4b", "test.m4b", 1000));

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>())).ReturnsAsync(updated);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1)).ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        await _controller.UpdateAudiobook(1, dto);

        _libraryConsistencyService.Verify(s => s.RecheckAudiobookAsync(1), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_ServiceThrows_ReturnsBadRequest()
    {
        var dto = MakeDto();

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()))
            .ThrowsAsync(new Exception("relocation failed"));

        var result = await _controller.UpdateAudiobook(1, dto);

        Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        var badRequest = (BadRequestObjectResult)result;
        Assert.AreEqual("relocation failed", badRequest.Value);
        _libraryConsistencyService.Verify(s => s.RecheckAudiobookAsync(It.IsAny<long>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAudiobook_ConsistencyRecheckThrows_StillReturnsOk()
    {
        // The controller swallows recheck failures (logged as a warning) so that a consistency-check
        // bug never masks a successful save.
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/Test Author/2024 - Test Book/test.m4b", "test.m4b", 1000));

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>())).ReturnsAsync(updated);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1))
            .ThrowsAsync(new Exception("recheck failed"));

        var result = await _controller.UpdateAudiobook(1, dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));
    }
}
