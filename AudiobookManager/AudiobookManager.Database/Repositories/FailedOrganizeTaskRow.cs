namespace AudiobookManager.Database.Repositories;

/// <summary>
/// A queued organize task row narrowed to what the failed-task list needs. Deliberately excludes
/// <c>JsonAudiobook</c> - the column that failed to deserialize is the whole reason this
/// projection exists (see <see cref="QueuedOrganizeTaskRepository.GetFailedQueuedOrganizeTasksAsync"/>),
/// so nothing here should try to parse it again.
/// </summary>
public record FailedOrganizeTaskRow(
    string OriginalFileLocation,
    DateTime QueuedTime,
    int FailureCount,
    string? LastFailureReason,
    DateTime? LastFailureAt);
