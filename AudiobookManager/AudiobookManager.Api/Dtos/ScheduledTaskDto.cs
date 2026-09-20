namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One row of the Settings "Tasks" page (GET api/settings/tasks). All timestamps are UTC on the
/// wire, matching this project's convention - the client formats them to local time.
/// </summary>
public record ScheduledTaskDto(
    string Key,
    string Name,
    string CronSchedule,
    bool Enabled,
    DateTime? LastRunAt,
    int? LastRunDurationMs,
    string? LastRunStatus,
    DateTime? NextRunAt);
