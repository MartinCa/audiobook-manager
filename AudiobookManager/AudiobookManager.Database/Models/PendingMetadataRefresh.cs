using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// The one pending metadata-refresh snapshot a book may hold: the values a scraper returned for
/// its source URL, stored until the user approves (applies via the normal edit/save flow) or
/// dismisses them. One row per book - a new refresh overwrites the old - which is why the
/// audiobook id carries a unique index: the primary key of the upsert is the book, not a row id.
/// </summary>
[Table("pending_metadata_refresh")]
public class PendingMetadataRefresh
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("audiobook_id")]
    public long AudiobookId { get; set; }

    public Audiobook Audiobook { get; set; } = null!;

    /// <summary>UTC timestamp of the fetch that produced this snapshot.</summary>
    [Required]
    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }

    /// <summary>Scraper source the snapshot came from (e.g. "Audible").</summary>
    [Required]
    [Column("source_name")]
    public string SourceName { get; set; } = string.Empty;

    /// <summary>The URL that was fetched.</summary>
    [Required]
    [Column("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Hand-versioned JSON payload; see Services.PendingRefreshPayload.</summary>
    [Required]
    [Column("payload_json")]
    public string PayloadJson { get; set; } = string.Empty;
}