using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class MissingTagsControllerTests
{
    private Mock<IMissingTagService> _missingTagService = null!;
    private Mock<ILanguageBackfillService> _backfillService = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private MissingTagsController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _missingTagService = new Mock<IMissingTagService>();
        _backfillService = new Mock<ILanguageBackfillService>();
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();

        var scope = new Mock<IServiceScope>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(ILanguageBackfillService)))
            .Returns(_backfillService.Object);
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        _controller = new MissingTagsController(
            _missingTagService.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            Mock.Of<IHostApplicationLifetime>(),
            Mock.Of<ILogger<MissingTagsController>>());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        // The gate is process-static, so a test that returned before the background task's
        // finally block ran would fail the next one with a spurious Conflict.
        await OperationGate.WaitUntilReleasedAsync(typeof(MissingTagsController));
    }

    /// <summary>
    /// The runner marks the operation finished and only then releases the static gate, so
    /// waiting on SetFinished alone can race the release - see the same pattern in
    /// SimilarValuesControllerTests.
    /// </summary>
    private Task RegisterFinishedWaiter()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _statusRegistry
            .Setup(r => r.SetFinished(MissingTagsController.LanguageBackfillOperationKey))
            .Callback(() => tcs.TrySetResult());
        return tcs.Task;
    }

    [TestMethod]
    public async Task StartLanguageBackfill_RunsTheBackfillAndReturnsOk()
    {
        var finished = RegisterFinishedWaiter();
        _backfillService
            .Setup(s => s.BackfillFromTagsAsync(It.IsAny<Func<string, int, int, Task>>()))
            .ReturnsAsync(new LanguageBackfillResult(3, 2, 1, 0));

        var result = _controller.StartLanguageBackfill();

        Assert.IsInstanceOfType(result, typeof(OkResult));
        await finished.WaitAsync(TimeSpan.FromSeconds(5));
        _backfillService.Verify(
            s => s.BackfillFromTagsAsync(It.IsAny<Func<string, int, int, Task>>()), Times.Once);
    }

    [TestMethod]
    public async Task StartLanguageBackfill_ReportsProgressToTheOperationStatusRegistry()
    {
        var finished = RegisterFinishedWaiter();
        _backfillService
            .Setup(s => s.BackfillFromTagsAsync(It.IsAny<Func<string, int, int, Task>>()))
            .Returns(async (Func<string, int, int, Task> progress) =>
            {
                await progress("Set en: a.m4b", 7, 12);
                return new LanguageBackfillResult(12, 7, 5, 0);
            });

        _controller.StartLanguageBackfill();

        await finished.WaitAsync(TimeSpan.FromSeconds(5));
        // The client follows this operation by polling the registry rather than over SignalR, so
        // the registry is the only place its progress is visible.
        _statusRegistry.Verify(
            r => r.SetProgress(MissingTagsController.LanguageBackfillOperationKey, 7, 12), Times.Once);
    }

    [TestMethod]
    public async Task StartLanguageBackfill_ReturnsConflictWhileOneIsAlreadyRunning()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = RegisterFinishedWaiter();
        _backfillService
            .Setup(s => s.BackfillFromTagsAsync(It.IsAny<Func<string, int, int, Task>>()))
            .Returns(async (Func<string, int, int, Task> _) =>
            {
                started.TrySetResult();
                await release.Task;
                return new LanguageBackfillResult(0, 0, 0, 0);
            });

        var first = _controller.StartLanguageBackfill();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = _controller.StartLanguageBackfill();

        Assert.IsInstanceOfType(first, typeof(OkResult));
        Assert.IsInstanceOfType(second, typeof(ConflictObjectResult));
        // Reading every untagged book's file header is a long pass; a second concurrent run would
        // do the same work twice.
        _backfillService.Verify(
            s => s.BackfillFromTagsAsync(It.IsAny<Func<string, int, int, Task>>()), Times.Once);

        release.TrySetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void GetFields_MapsServiceFieldsToDtos()
    {
        _missingTagService
            .Setup(s => s.GetTaggableFields())
            .Returns(new List<MissingTagField> { new("Language", "Language", false) });

        var fields = _controller.GetFields();

        Assert.AreEqual(1, fields.Count);
        Assert.AreEqual("Language", fields[0].Key);
        Assert.AreEqual("Language", fields[0].Label);
        Assert.IsFalse(fields[0].IsCriticalByDefault);
    }
}
