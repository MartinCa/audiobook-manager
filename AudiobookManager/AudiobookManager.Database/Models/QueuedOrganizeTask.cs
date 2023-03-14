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

    public QueuedOrganizeTask(string originalFileLocation, string jsonAudiobook, DateTime queuedTime)
    {
        OriginalFileLocation = originalFileLocation;
        JsonAudiobook = jsonAudiobook;
        QueuedTime = queuedTime;
    }
}
