using AudiobookManager.Api.Async;
using AudiobookManager.Api.Workers;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainAudiobookFileInfo = AudiobookManager.Domain.AudiobookFileInfo;
using DomainPerson = AudiobookManager.Domain.Person;
using QueuedOrganizeTask = AudiobookManager.Domain.QueuedOrganizeTask;

namespace AudiobookManager.Test.Workers;

[TestClass]
public class OrganizeWorkerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IOrganize> _organizeClient = null!;
    private Mock<IQueuedOrganizeTaskService> _organizeTaskService = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<IDiscoveredAudiobookRepository> _discoveredRepo = null!;
    private Mock<ILogger<OrganizeWorker>> _logger = null!;
    private ServiceProvider _serviceProvider = null!;
    private OrganizeWorker _worker = null!;

    [TestInitialize]
    public void Setup()
    {
        _organizeClient = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(_organizeClient.Object);
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        _organizeTaskService = new Mock<IQueuedOrganizeTaskService>();
        _audiobookService = new Mock<IAudiobookService>();
        _discoveredRepo = new Mock<IDiscoveredAudiobookRepository>();

        var services = new ServiceCollection();
        services.AddSingleton(_organizeTaskService.Object);
        services.AddSingleton(_audiobookService.Object);
        services.AddSingleton(_discoveredRepo.Object);
        _serviceProvider = services.BuildServiceProvider();

        _logger = new Mock<ILogger<OrganizeWorker>>();
        _worker = new OrganizeWorker(_hubContext.Object, _serviceProvider, _logger.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _serviceProvider.Dispose();
    }

    private static DomainAudiobook MakeAudiobook(string sourcePath) =>
        new DomainAudiobook(
            new List<DomainPerson> { new DomainPerson("Author") },
            "A Book",
            2020,
            new DomainAudiobookFileInfo(sourcePath, Path.GetFileName(sourcePath), 1000));

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

    [TestMethod]
    public async Task ExecuteAsync_OrganizeSucceeds_DeletesTheDiscoveredRowForTheOriginalFileLocation()
    {
        const string originalPath = "/import/book.m4b";
        var task = new QueuedOrganizeTask(originalPath, MakeAudiobook(originalPath), DateTime.UtcNow);
        var dequeueCount = 0;
        _organizeTaskService.Setup(s => s.GetNextQueuedOrganizeTask())
            .ReturnsAsync(() => dequeueCount++ == 0 ? task : null);
        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ReturnsAsync((DomainAudiobook a, Func<string, int, Task> _) => a);

        await _worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => dequeueCount >= 1, TimeSpan.FromSeconds(5));
        await _worker.StopAsync(CancellationToken.None);

        _organizeTaskService.Verify(s => s.DeleteQueuedOrganizeTask(originalPath), Times.Once);
        _discoveredRepo.Verify(r => r.DeleteByPathAsync(originalPath), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_OrganizeFails_LeavesTheDiscoveredRowInPlaceAndReportsQueueError()
    {
        const string originalPath = "/import/book.m4b";
        var task = new QueuedOrganizeTask(originalPath, MakeAudiobook(originalPath), DateTime.UtcNow);
        var dequeueCount = 0;
        _organizeTaskService.Setup(s => s.GetNextQueuedOrganizeTask())
            .ReturnsAsync(() => dequeueCount++ == 0 ? task : null);
        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ThrowsAsync(new Exception("'/library/book.m4b' already exists"));

        await _worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => dequeueCount >= 1, TimeSpan.FromSeconds(5));
        await _worker.StopAsync(CancellationToken.None);

        _discoveredRepo.Verify(r => r.DeleteByPathAsync(It.IsAny<string>()), Times.Never);
        _organizeTaskService.Verify(s => s.DeleteQueuedOrganizeTask(originalPath), Times.Once);
        _organizeClient.Verify(c => c.QueueError(It.Is<QueueError>(e =>
            e.OriginalFileLocation == originalPath && e.Error == "'/library/book.m4b' already exists")), Times.Once);
    }

    // The busy-loop: the five-second idle delay covered only the "queue is empty" path, so a
    // throwing queue read left `task` null, logged, and came straight back round - a pinned core
    // and a log filling at thousands of lines per second, forever for a row that cannot be read.
    [TestMethod]
    public async Task ExecuteAsync_TheQueueReadKeepsThrowing_BacksOffInsteadOfSpinning()
    {
        var attempts = 0;
        _organizeTaskService.Setup(s => s.GetNextQueuedOrganizeTask())
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("database is locked");
            });

        await _worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 2, TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromSeconds(2));
        await _worker.StopAsync(CancellationToken.None);

        // Bounded rather than exact: the assertion is that the loop is paced at all. Unpaced, two
        // seconds of a failing read is thousands of attempts; with backoff (1s, then 2s, ...) it
        // is a handful.
        var observed = Volatile.Read(ref attempts);
        Assert.IsTrue(observed >= 2, $"Expected the worker to retry at least once, saw {observed} attempt(s).");
        Assert.IsTrue(observed < 20, $"Expected the failing read to be paced, saw {observed} attempts in about 3 seconds.");
    }

    // The backoff must not become a permanent penalty: a queue read that fails while a scan holds
    // the database has to be answered promptly again once the scan finishes.
    [TestMethod]
    public async Task ExecuteAsync_AfterAFailureRecovers_ReturnsToTheNormalIdleCadence()
    {
        var attempts = 0;
        _organizeTaskService.Setup(s => s.GetNextQueuedOrganizeTask())
            .ReturnsAsync(() =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    throw new InvalidOperationException("database is locked");
                }

                return null;
            });

        await _worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 2, TimeSpan.FromSeconds(10));
        await _worker.StopAsync(CancellationToken.None);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Only the one genuine failure should have been logged as an error.");
    }

    [TestMethod]
    public async Task ExecuteAsync_CooperativeShutdownWhileIdle_DoesNotLogAnError()
    {
        // No task is ever queued, so the worker sits in its idle `Task.Delay(5s, stoppingToken)`.
        // Stopping the host should cancel that delay and exit cleanly, without treating the
        // resulting OperationCanceledException as a processing failure.
        var pollCount = 0;
        _organizeTaskService.Setup(s => s.GetNextQueuedOrganizeTask())
            .ReturnsAsync(() => { pollCount++; return null; });

        await _worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => pollCount >= 1, TimeSpan.FromSeconds(5));
        await _worker.StopAsync(CancellationToken.None);

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
