using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One series' most recent failed metadata refresh (single or bulk) - bookkeeping-only, mirroring
/// <see cref="BookConsistencyIssueType.MetadataRefreshFailed"/>'s shape and semantics but scoped
/// to a series instead of a book. At most one row per series: a fresh failure replaces the
/// previous error rather than accumulating a history, and a subsequent successful refresh deletes
/// the row.
/// </summary>
[Table("series_consistency_issues")]
public class SeriesConsistencyIssue
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("series_id")]
    public long SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    [Required]
    [Column("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [Required]
    [Column("detected_at")]
    public DateTime DetectedAt { get; set; }
}
