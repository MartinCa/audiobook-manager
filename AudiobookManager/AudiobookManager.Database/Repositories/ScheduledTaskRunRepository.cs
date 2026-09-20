using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class ScheduledTaskRunRepository : IScheduledTaskRunRepository
{
    private readonly DatabaseContext _db;

    public ScheduledTaskRunRepository(DatabaseContext db)
    {
        _db = db;
    }

    public Task<ScheduledTaskRun?> GetAsync(string taskKey) =>
        _db.ScheduledTaskRuns.AsNoTracking().SingleOrDefaultAsync(r => r.TaskKey == taskKey);

    /// <summary>
    /// Get-or-create-then-update, one row per task key. The worker's own tick and the manual
    /// "Check Now" endpoint share <c>UpcomingReleasesController.RefreshGate</c>, so they cannot
    /// race against each other in practice - but nothing stops a second, differently-keyed task
    /// added later from racing this same method against itself, so the guard is not skipped just
    /// because today's only caller is gated elsewhere (see AGENTS.md's get-or-create race section,
    /// same adopt-the-winner pattern as LibrarySettingsRepository/PersonRepository.GetOrCreatePersons).
    /// </summary>
    public async Task RecordRunAsync(string taskKey, DateTime startedAtUtc, int durationMs, bool succeeded, string? error)
    {
        var status = succeeded ? "Success" : "Failed";
        var run = await _db.ScheduledTaskRuns.SingleOrDefaultAsync(r => r.TaskKey == taskKey);
        if (run == null)
        {
            run = new ScheduledTaskRun(taskKey);
            _db.ScheduledTaskRuns.Add(run);
        }

        run.LastRunStartedAt = startedAtUtc;
        run.LastRunDurationMs = durationMs;
        run.LastRunStatus = status;
        run.LastRunError = error;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // The insert raced on task_key: another writer created the row first, and this
            // instance must apply its update to the winner's row instead of surfacing a UNIQUE
            // violation.
            _db.Entry(run).State = EntityState.Detached;

            var winner = await _db.ScheduledTaskRuns.SingleAsync(r => r.TaskKey == taskKey);
            winner.LastRunStartedAt = startedAtUtc;
            winner.LastRunDurationMs = durationMs;
            winner.LastRunStatus = status;
            winner.LastRunError = error;
            await _db.SaveChangesAsync();
        }
    }
}
