using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class AudiobookControllerTests
{
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<IQueuedOrganizeTaskService> _organizeTaskService = null!;
    private Mock<ILibraryConsistencyService> _libraryConsistencyService = null!;
    private Mock<IOrganize> _organizeClient = null!;
    private Mock<ILogger<AudiobookController>> _logger = null!;
    private ServiceProvider _serviceProvider = null!;
    private AudiobookController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookService = new Mock<IAudiobookService>();
        _organizeTaskService = new Mock<IQueuedOrganizeTaskService>();
        _libraryConsistencyService = new Mock<ILibraryConsistencyService>();
        _logger = new Mock<ILogger<AudiobookController>>();

        _organizeClient = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(_organizeClient.Object);
        var organizeHub = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        organizeHub.Setup(h => h.Clients).Returns(clients.Object);

        // UpdateAudiobook is fire-and-forget: it resolves its own services from a fresh DI scope
        // rather than the controller's constructor-injected (request-scoped) instances, so route
        // the same mocks through a real ServiceProvider for it to resolve.
        var services = new ServiceCollection();
        services.AddSingleton(_audiobookService.Object);
        services.AddSingleton(_libraryConsistencyService.Object);
        _serviceProvider = services.BuildServiceProvider();

        _controller = new AudiobookController(
            _audiobookService.Object,
            _organizeTaskService.Object,
            _libraryConsistencyService.Object,
            organizeHub.Object,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new AudiobookSaveGate(),
            _logger.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(20);
        }

        Assert.Fail("Condition was not met within the timeout.");
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
    public async Task CheckTargetPath_NoCollision_ReturnsExistsFalse()
    {
        var dto = MakeDto();

        _audiobookService.Setup(s => s.CheckTargetPathCollision(It.IsAny<Audiobook>()))
            .ReturnsAsync(new TargetPathCollisionResult { TargetPath = "/library/Test Author/2024 - Test Book/test.m4b", Exists = false });

        var result = await _controller.CheckTargetPath(dto);

        Assert.AreEqual("/library/Test Author/2024 - Test Book/test.m4b", result.TargetPath);
        Assert.IsFalse(result.Exists);
        Assert.IsNull(result.Existing);
    }

    [TestMethod]
    public async Task CheckTargetPath_Collision_ReturnsExistingFileDetails()
    {
        var dto = MakeDto();

        _audiobookService.Setup(s => s.CheckTargetPathCollision(It.IsAny<Audiobook>()))
            .ReturnsAsync(new TargetPathCollisionResult
            {
                TargetPath = "/library/Test Author/2024 - Test Book/test.m4b",
                Exists = true,
                ExistingAudiobookId = 7,
                ExistingSizeInBytes = 598_000_000,
                ExistingDurationInSeconds = 39600
            });

        var result = await _controller.CheckTargetPath(dto);

        Assert.IsTrue(result.Exists);
        Assert.IsNotNull(result.Existing);
        Assert.AreEqual(7, result.Existing!.AudiobookId);
        Assert.AreEqual(598_000_000, result.Existing.SizeInBytes);
        Assert.AreEqual(39600, result.Existing.DurationInSeconds);
    }

    [TestMethod]
    public async Task UpdateAudiobook_ReturnsOkImmediatelyAndDelegatesToAudiobookServiceUpdateAudiobookInBackground()
    {
        // Regression guard for the CLAUDE.md binding invariant: Author/Series/SeriesPart/Year/BookName
        // edits must always flow through AudiobookService.UpdateAudiobook, never a raw repository call.
        // UpdateAudiobook is fire-and-forget (matching OrganizeAudiobook/BookOrganize.vue's SignalR
        // progress pattern), so the call itself completes and finishes the background work asynchronously.
        var dto = MakeDto("Updated Book Name", series: "Some Series", seriesPart: "2");

        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Updated Book Name",
            2024,
            new AudiobookFileInfo("/library/Test Author/Some Series/Book 02 - 2024 - Updated Book Name/test.m4b", "test.m4b", 1000));

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>())).ReturnsAsync(updated);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1)).ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        var result = _controller.UpdateAudiobook(1, dto);

        Assert.IsInstanceOfType(result, typeof(OkResult));

        await WaitUntilAsync(() =>
            _audiobookService.Invocations.Any(i => i.Method.Name == nameof(IAudiobookService.UpdateAudiobook)),
            TimeSpan.FromSeconds(5));

        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.Is<Audiobook>(a =>
            a.BookName == "Updated Book Name" &&
            a.Series == "Some Series" &&
            a.SeriesPart == "2" &&
            a.Year == 2024), It.IsAny<Func<string, int, Task>>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_ReportsProgressAndCompleteOverSignalR()
    {
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/Test Author/2024 - Test Book/test.m4b", "test.m4b", 1000));

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Returns((long id, Audiobook a, Func<string, int, Task> progressAction) => InvokeProgressThenReturn(progressAction, updated));
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1)).ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        _controller.UpdateAudiobook(1, dto);

        await WaitUntilAsync(() =>
            _organizeClient.Invocations.Any(i => i.Method.Name == nameof(IOrganize.AudiobookSaveComplete)),
            TimeSpan.FromSeconds(5));

        _organizeClient.Verify(c => c.AudiobookSaveProgress(It.Is<AudiobookSaveProgress>(p =>
            p.AudiobookId == 1 && p.ProgressMessage == "Started" && p.Progress == 0)), Times.Once);
        _organizeClient.Verify(c => c.AudiobookSaveComplete(It.Is<AudiobookSaveComplete>(r => r.AudiobookId == 1)), Times.Once);
        _libraryConsistencyService.Verify(s => s.RecheckAudiobookAsync(1), Times.Once);
    }

    private static async Task<Audiobook> InvokeProgressThenReturn(Func<string, int, Task> progressAction, Audiobook result)
    {
        await progressAction("Started", 0);
        return result;
    }

    [TestMethod]
    public async Task UpdateAudiobook_ServiceThrows_ReportsAudiobookSaveErrorOverSignalR()
    {
        var dto = MakeDto();

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ThrowsAsync(new Exception("relocation failed"));

        _controller.UpdateAudiobook(1, dto);

        await WaitUntilAsync(() =>
            _organizeClient.Invocations.Any(i => i.Method.Name == nameof(IOrganize.AudiobookSaveError)),
            TimeSpan.FromSeconds(5));

        _organizeClient.Verify(c => c.AudiobookSaveError(It.Is<AudiobookSaveError>(e =>
            e.AudiobookId == 1 && e.Error == "relocation failed")), Times.Once);
        _organizeClient.Verify(c => c.AudiobookSaveComplete(It.IsAny<AudiobookSaveComplete>()), Times.Never);
        _libraryConsistencyService.Verify(s => s.RecheckAudiobookAsync(It.IsAny<long>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAudiobook_ConsistencyRecheckThrows_StillReportsSaveComplete()
    {
        // The background work swallows recheck failures (logged as a warning) so that a
        // consistency-check bug never masks a successful save.
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/Test Author/2024 - Test Book/test.m4b", "test.m4b", 1000));

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>())).ReturnsAsync(updated);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1))
            .ThrowsAsync(new Exception("recheck failed"));

        _controller.UpdateAudiobook(1, dto);

        await WaitUntilAsync(() =>
            _organizeClient.Invocations.Any(i => i.Method.Name == nameof(IOrganize.AudiobookSaveComplete)),
            TimeSpan.FromSeconds(5));

        _organizeClient.Verify(c => c.AudiobookSaveComplete(It.Is<AudiobookSaveComplete>(r => r.AudiobookId == 1)), Times.Once);
        _organizeClient.Verify(c => c.AudiobookSaveError(It.IsAny<AudiobookSaveError>()), Times.Never);
    }

    // A save's progress and completion are broadcast over SignalR, so a client that was
    // disconnected while it finished never sees the completion and would sit disabled forever.
    // This endpoint is how the editor recovers on reconnect. Distinct ids per test, since the
    // in-flight set is process-static and outlives a single test.
    [TestMethod]
    public async Task GetSaveStatus_WhileASaveIsRunning_ReportsSaving()
    {
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/test.m4b", "test.m4b", 1000));

        var saveStarted = new TaskCompletionSource();
        var releaseSave = new TaskCompletionSource();

        _audiobookService
            .Setup(s => s.UpdateAudiobook(202, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Returns(async () =>
            {
                saveStarted.TrySetResult();
                await releaseSave.Task;
                return updated;
            });
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(202))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        Assert.IsFalse(_controller.GetSaveStatus(202).IsSaving, "nothing in flight before the save starts");

        try
        {
            _controller.UpdateAudiobook(202, dto);
            await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var status = _controller.GetSaveStatus(202);
            Assert.AreEqual(202, status.AudiobookId);
            Assert.IsTrue(status.IsSaving);
        }
        finally
        {
            releaseSave.TrySetResult();
        }

        // Poll the real release condition rather than assuming the background finally has run.
        await WaitUntilAsync(() => !_controller.GetSaveStatus(202).IsSaving, TimeSpan.FromSeconds(5));
        Assert.IsFalse(_controller.GetSaveStatus(202).IsSaving);
    }

    [TestMethod]
    public void GetSaveStatus_ForABookWithNoSaveInFlight_ReportsNotSaving()
    {
        var status = _controller.GetSaveStatus(9999);

        Assert.AreEqual(9999, status.AudiobookId);
        Assert.IsFalse(status.IsSaving);
    }

    [TestMethod]
    public async Task UpdateAudiobook_SecondSaveForTheSameBookWhileTheFirstIsRunning_IsRejectedAsAConflict()
    {
        // Regression: the endpoint ran its work in a bare Task.Run with no gate, so two saves for
        // the same book (a double-clicked Save, or a save racing a similar-value alignment) both
        // read the same pre-move path - the first relocates the file and the second then writes
        // tags to a path that no longer exists, or fails with a spurious "already exists".
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/test.m4b", "test.m4b", 1000));

        var firstSaveStarted = new TaskCompletionSource();
        var releaseFirstSave = new TaskCompletionSource();

        _audiobookService
            .Setup(s => s.UpdateAudiobook(101, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Returns(async () =>
            {
                firstSaveStarted.TrySetResult();
                await releaseFirstSave.Task;
                return updated;
            });
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(101))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        try
        {
            var first = _controller.UpdateAudiobook(101, dto);
            Assert.IsInstanceOfType(first, typeof(OkResult));

            // Wait for the real condition - the save being in flight - rather than a fixed delay.
            await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = _controller.UpdateAudiobook(101, dto);
            Assert.IsInstanceOfType(second, typeof(ConflictObjectResult));
        }
        finally
        {
            releaseFirstSave.TrySetResult();
        }

        await WaitUntilAsync(
            () => _audiobookService.Invocations.Count(i => i.Method.Name == nameof(IAudiobookService.UpdateAudiobook)) == 1,
            TimeSpan.FromSeconds(5));

        _audiobookService.Verify(
            s => s.UpdateAudiobook(101, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()),
            Times.Once);

        // Poll the real condition (the gate being free) rather than assuming the background
        // task's finally block has already run - see OperationGate for the same reasoning.
        await WaitUntilAsync(
            () => _controller.UpdateAudiobook(101, dto) is OkResult,
            TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task UpdateAudiobook_ConcurrentSavesForDifferentBooks_BothProceed()
    {
        // The gate is per-audiobook: unrelated books touch unrelated files and must not block
        // each other the way a single global lock would.
        var dto = MakeDto();
        var updated = new Audiobook(
            new List<Person> { new Person("Test Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/test.m4b", "test.m4b", 1000));

        var bookOneStarted = new TaskCompletionSource();
        var releaseBookOne = new TaskCompletionSource();

        _audiobookService
            .Setup(s => s.UpdateAudiobook(102, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Returns(async () =>
            {
                bookOneStarted.TrySetResult();
                await releaseBookOne.Task;
                return updated;
            });
        _audiobookService
            .Setup(s => s.UpdateAudiobook(103, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ReturnsAsync(updated);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(It.IsAny<long>()))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        try
        {
            Assert.IsInstanceOfType(_controller.UpdateAudiobook(102, dto), typeof(OkResult));
            await bookOneStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsInstanceOfType(_controller.UpdateAudiobook(103, dto), typeof(OkResult));

            await WaitUntilAsync(
                () => _audiobookService.Invocations.Any(i =>
                    i.Method.Name == nameof(IAudiobookService.UpdateAudiobook) && (long)i.Arguments[0] == 103L),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseBookOne.TrySetResult();
        }
    }

    [TestMethod]
    public async Task UpdateAudiobook_AfterAFailedSave_TheGateIsReleasedSoTheBookCanBeSavedAgain()
    {
        var dto = MakeDto();

        _audiobookService
            .Setup(s => s.UpdateAudiobook(104, It.IsAny<Audiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ThrowsAsync(new Exception("save blew up"));

        Assert.IsInstanceOfType(_controller.UpdateAudiobook(104, dto), typeof(OkResult));

        await WaitUntilAsync(
            () => _organizeClient.Invocations.Any(i => i.Method.Name == nameof(IOrganize.AudiobookSaveError)),
            TimeSpan.FromSeconds(5));

        // A save that threw must not leave the book permanently un-saveable.
        await WaitUntilAsync(
            () => _controller.UpdateAudiobook(104, dto) is OkResult,
            TimeSpan.FromSeconds(5));
    }
}
