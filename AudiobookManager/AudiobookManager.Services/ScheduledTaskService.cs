using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using Cronos;

namespace AudiobookManager.Services;

public class ScheduledTaskService : IScheduledTaskService
{
    /// <summary>
    /// The known scheduled tasks. Only one exists today; a future task gets its own entry here
    /// plus a cron/enabled source read into <see cref="GetScheduledTasksAsync"/> below - the
    /// <c>scheduled_task_runs</c> table already keys by task, so no schema change is needed.
    /// </summary>
    private static readonly IReadOnlyList<(string Key, string Name)> RegisteredTasks =
    [
        (ScheduledTaskKeys.UpcomingReleasesRefresh, "Upcoming Releases Refresh"),
    ];

    private readonly ISettingsService _settingsService;
    private readonly IScheduledTaskRunRepository _runRepository;

    public ScheduledTaskService(ISettingsService settingsService, IScheduledTaskRunRepository runRepository)
    {
        _settingsService = settingsService;
        _runRepository = runRepository;
    }

    public async Task<IReadOnlyList<Domain.ScheduledTask>> GetScheduledTasksAsync()
    {
        // Only one task exists today, and its schedule lives on LibrarySettings - fetched once
        // and reused for every registered task rather than once per task, since they all
        // currently share it.
        var librarySettings = await _settingsService.GetLibrarySettings();

        var result = new List<Domain.ScheduledTask>(RegisteredTasks.Count);
        foreach (var (key, name) in RegisteredTasks)
        {
            var (cron, enabled) = GetScheduleFor(key, librarySettings);
            var run = await _runRepository.GetAsync(key);

            DateTime? nextRunAt = null;
            if (enabled && CronExpression.TryParse(cron, CronFormat.Standard, out var parsed))
            {
                nextRunAt = parsed!.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc)?.UtcDateTime;
            }

            result.Add(new Domain.ScheduledTask(
                key,
                name,
                cron,
                enabled,
                run?.LastRunStartedAt,
                run?.LastRunDurationMs,
                run?.LastRunStatus,
                nextRunAt));
        }

        return result;
    }

    public Task RecordTaskRunAsync(string taskKey, DateTime startedAtUtc, TimeSpan duration, bool succeeded, string? error) =>
        _runRepository.RecordRunAsync(taskKey, startedAtUtc, (int)duration.TotalMilliseconds, succeeded, error);

    private static (string Cron, bool Enabled) GetScheduleFor(string taskKey, Domain.LibrarySettings librarySettings) => taskKey switch
    {
        ScheduledTaskKeys.UpcomingReleasesRefresh =>
            (librarySettings.UpcomingReleasesCronSchedule, librarySettings.UpcomingReleasesEnabled),
        _ => throw new ArgumentOutOfRangeException(nameof(taskKey), taskKey, "Unknown scheduled task key"),
    };
}
