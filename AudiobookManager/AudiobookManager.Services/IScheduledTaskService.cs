namespace AudiobookManager.Services;

public interface IScheduledTaskService
{
    /// <summary>Every registered scheduled task, with its current schedule and last-run outcome.</summary>
    Task<IReadOnlyList<Domain.ScheduledTask>> GetScheduledTasksAsync();

    /// <summary>Records the outcome of a run of the given task, for display on the Tasks page.</summary>
    Task RecordTaskRunAsync(string taskKey, DateTime startedAtUtc, TimeSpan duration, bool succeeded, string? error);
}
