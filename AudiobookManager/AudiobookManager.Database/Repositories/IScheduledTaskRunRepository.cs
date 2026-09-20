using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IScheduledTaskRunRepository
{
    /// <summary>The task's last-run row, or null if it has never run.</summary>
    Task<ScheduledTaskRun?> GetAsync(string taskKey);

    /// <summary>Upserts the task's last-run row with the outcome of the run that just finished.</summary>
    Task RecordRunAsync(string taskKey, DateTime startedAtUtc, int durationMs, bool succeeded, string? error);
}
