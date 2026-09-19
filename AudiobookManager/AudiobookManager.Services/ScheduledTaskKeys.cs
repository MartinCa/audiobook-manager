namespace AudiobookManager.Services;

/// <summary>
/// Task keys for <c>scheduled_task_runs</c> and <see cref="IScheduledTaskService"/>'s registry -
/// a single source so the worker that runs a task and the controller/endpoint that reports on it
/// never drift onto different magic strings for the same row.
/// </summary>
public static class ScheduledTaskKeys
{
    public const string UpcomingReleasesRefresh = "upcoming_releases_refresh";
}
