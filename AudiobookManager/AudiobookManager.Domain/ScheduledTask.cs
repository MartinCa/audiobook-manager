namespace AudiobookManager.Domain;

/// <summary>
/// One entry on the Settings "Tasks" page: a registered background task's schedule plus its most
/// recent run, as served by <c>GET api/settings/tasks</c>. <see cref="NextRunAt"/> is null when
/// the task is disabled or its stored cron expression fails to parse - both are dead ends for
/// "when will this run next", not errors to surface as a broken page.
/// </summary>
public record ScheduledTask(
    string Key,
    string Name,
    string CronSchedule,
    bool Enabled,
    DateTime? LastRunAt,
    int? LastRunDurationMs,
    string? LastRunStatus,
    DateTime? NextRunAt);
