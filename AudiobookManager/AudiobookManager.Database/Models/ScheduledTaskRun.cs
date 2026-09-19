using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// The most recent run of one registered scheduled task, keyed by a hand-maintained task key
/// (<c>Services.ScheduledTaskKeys</c>) rather than a singleton row - there is room to register more
/// scheduled tasks later without a schema change. One row per task key, overwritten on every run
/// rather than kept as history: the Settings "Tasks" page only ever shows the latest run.
/// </summary>
[Table("scheduled_task_runs")]
public class ScheduledTaskRun
{
    [Key]
    [Column("task_key")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string TaskKey { get; set; } = string.Empty;

    [Column("last_run_started_at")]
    public DateTime? LastRunStartedAt { get; set; }

    [Column("last_run_duration_ms")]
    public int? LastRunDurationMs { get; set; }

    /// <summary>"Success" or "Failed" - never parsed back into an enum, just displayed.</summary>
    [Column("last_run_status")]
    public string? LastRunStatus { get; set; }

    /// <summary>
    /// The failing exception's message, stored server-side for the operator's own Settings page -
    /// not a client-facing 500 response, so the "never leak exception detail" convention does not
    /// apply here (see AGENTS.md's ProblemResults guidance, which is about API responses).
    /// </summary>
    [Column("last_run_error")]
    public string? LastRunError { get; set; }

    public ScheduledTaskRun() { }

    public ScheduledTaskRun(string taskKey)
    {
        TaskKey = taskKey;
    }
}
