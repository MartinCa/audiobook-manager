using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DomainLibrarySettings = AudiobookManager.Domain.LibrarySettings;

namespace AudiobookManager.Test.Services;

[TestClass]
public class ScheduledTaskServiceTests
{
    private Mock<ISettingsService> _settingsService = null!;
    private Mock<IScheduledTaskRunRepository> _runRepository = null!;
    private ScheduledTaskService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _settingsService = new Mock<ISettingsService>();
        _runRepository = new Mock<IScheduledTaskRunRepository>();
        _service = new ScheduledTaskService(_settingsService.Object, _runRepository.Object);
    }

    [TestMethod]
    public async Task GetScheduledTasksAsync_ReadsCronAndEnabledFromLibrarySettings()
    {
        _settingsService
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new DomainLibrarySettings
            {
                UpcomingReleasesEnabled = true,
                UpcomingReleasesCronSchedule = "0 3 * * *",
            });
        _runRepository.Setup(r => r.GetAsync(ScheduledTaskKeys.UpcomingReleasesRefresh)).ReturnsAsync((ScheduledTaskRun?)null);

        var tasks = await _service.GetScheduledTasksAsync();

        var task = tasks.Single(t => t.Key == ScheduledTaskKeys.UpcomingReleasesRefresh);
        Assert.AreEqual("Upcoming Releases Refresh", task.Name);
        Assert.AreEqual("0 3 * * *", task.CronSchedule);
        Assert.IsTrue(task.Enabled);
    }

    [TestMethod]
    public async Task GetScheduledTasksAsync_NeverRun_LastRunFieldsAreNull()
    {
        _settingsService.Setup(s => s.GetLibrarySettings()).ReturnsAsync(new DomainLibrarySettings());
        _runRepository.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((ScheduledTaskRun?)null);

        var tasks = await _service.GetScheduledTasksAsync();

        var task = tasks.Single();
        Assert.IsNull(task.LastRunAt);
        Assert.IsNull(task.LastRunDurationMs);
        Assert.IsNull(task.LastRunStatus);
    }

    [TestMethod]
    public async Task GetScheduledTasksAsync_HasARun_ServesTheStoredOutcome()
    {
        _settingsService.Setup(s => s.GetLibrarySettings()).ReturnsAsync(new DomainLibrarySettings());
        var startedAt = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        _runRepository
            .Setup(r => r.GetAsync(ScheduledTaskKeys.UpcomingReleasesRefresh))
            .ReturnsAsync(new ScheduledTaskRun(ScheduledTaskKeys.UpcomingReleasesRefresh)
            {
                LastRunStartedAt = startedAt,
                LastRunDurationMs = 4200,
                LastRunStatus = "Success",
            });

        var task = (await _service.GetScheduledTasksAsync()).Single();

        Assert.AreEqual(startedAt, task.LastRunAt);
        Assert.AreEqual(4200, task.LastRunDurationMs);
        Assert.AreEqual("Success", task.LastRunStatus);
    }

    [TestMethod]
    public async Task GetScheduledTasksAsync_Disabled_NextRunIsNull()
    {
        _settingsService
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new DomainLibrarySettings { UpcomingReleasesEnabled = false, UpcomingReleasesCronSchedule = "0 3 * * *" });
        _runRepository.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((ScheduledTaskRun?)null);

        var task = (await _service.GetScheduledTasksAsync()).Single();

        Assert.IsFalse(task.Enabled);
        Assert.IsNull(task.NextRunAt);
    }

    [TestMethod]
    public async Task GetScheduledTasksAsync_Enabled_NextRunIsInTheFuture()
    {
        _settingsService
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new DomainLibrarySettings { UpcomingReleasesEnabled = true, UpcomingReleasesCronSchedule = "0 3 * * *" });
        _runRepository.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((ScheduledTaskRun?)null);

        var task = (await _service.GetScheduledTasksAsync()).Single();

        Assert.IsNotNull(task.NextRunAt);
        Assert.IsTrue(task.NextRunAt > DateTime.UtcNow);
    }

    // Defensive only - SettingsController.UpdateLibrarySettings already refuses an unparsable
    // cron on write, but a hand-edited database row must not crash the Tasks page.
    [TestMethod]
    public async Task GetScheduledTasksAsync_UnparsableStoredCron_NextRunIsNullNotAnException()
    {
        _settingsService
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new DomainLibrarySettings { UpcomingReleasesEnabled = true, UpcomingReleasesCronSchedule = "garbage" });
        _runRepository.Setup(r => r.GetAsync(It.IsAny<string>())).ReturnsAsync((ScheduledTaskRun?)null);

        var task = (await _service.GetScheduledTasksAsync()).Single();

        Assert.IsNull(task.NextRunAt);
    }

    [TestMethod]
    public async Task RecordTaskRunAsync_DelegatesToTheRepositoryWithMillisecondDuration()
    {
        var startedAt = DateTime.UtcNow;

        await _service.RecordTaskRunAsync(
            ScheduledTaskKeys.UpcomingReleasesRefresh, startedAt, TimeSpan.FromSeconds(2.5), succeeded: true, error: null);

        _runRepository.Verify(
            r => r.RecordRunAsync(ScheduledTaskKeys.UpcomingReleasesRefresh, startedAt, 2500, true, null),
            Times.Once);
    }
}
