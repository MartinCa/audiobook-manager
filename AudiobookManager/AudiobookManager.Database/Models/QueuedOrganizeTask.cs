using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("queued_organize_task")]
public class QueuedOrganizeTask
{
    [Key]
    [Required]
    [Column("original_file_location")]
    public string OriginalFileLocation { get; set; }

    [Required]
    [Column("json_audiobook")]
    public string JsonAudiobook { get; set; }

    [Required]
    [Column("queued_time")]
    public DateTime QueuedTime { get; set; }

    // Tracks a row that GetNextQueuedOrganizeTask picked but whose json_audiobook could not be
    // deserialised back into an Audiobook - a breaking change to that shape (a renamed/removed
    // property) makes every row queued before the upgrade undeserialisable after it. Without this,
    // the same unreadable row is picked again on every iteration and blocks the queue forever
    // (see #1322). Once FailureCount reaches the repository's threshold the row is excluded from
    // GetNextQueuedOrganizeTask, so the queue moves on - the row itself is left in place rather
    // than deleted, since its json_audiobook may be the only surviving copy of edits the user made
    // before queuing.
    [Required]
    [Column("failure_count")]
    public int FailureCount { get; set; }

    [Column("last_failure_reason")]
    public string? LastFailureReason { get; set; }

    [Column("last_failure_at")]
    public DateTime? LastFailureAt { get; set; }

    public QueuedOrganizeTask(string originalFileLocation, string jsonAudiobook, DateTime queuedTime)
    {
        OriginalFileLocation = originalFileLocation;
        JsonAudiobook = jsonAudiobook;
        QueuedTime = queuedTime;
    }
}
