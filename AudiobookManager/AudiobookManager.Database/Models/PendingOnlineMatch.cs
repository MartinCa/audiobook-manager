using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

public enum PendingOnlineMatchStatus
{
    Pending = 0,
    Rejected = 1,
}

/// <summary>
/// One book's outstanding bulk online-metadata-search result set: a snapshot of the candidates a
/// background search fanned out for, awaiting the user picking one (which hands it to the normal
/// pending-metadata-refresh apply flow, see <see cref="Services.MetadataRefreshService"/>) or
/// rejecting it. One row per book - a later search for the same book overwrites the old row - the
/// audiobook id therefore carries a unique index, the same "book is the upsert key" shape
/// <see cref="PendingMetadataRefresh"/> uses.
/// </summary>
[Table("pending_online_match")]
public class PendingOnlineMatch
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("audiobook_id")]
    public long AudiobookId { get; set; }

    public Audiobook Audiobook { get; set; } = null!;

    /// <summary>UTC timestamp of the search that produced this row's candidates.</summary>
    [Required]
    [Column("searched_at")]
    public DateTime SearchedAt { get; set; }

    /// <summary>
    /// Pending (awaiting a selection or a reject) or Rejected (moved to the Failed/Rejected list).
    /// Stored as the enum's underlying int - EF's default, matching every other stored enum column
    /// in this codebase (e.g. BookConsistencyIssue.IssueType).
    /// </summary>
    [Required]
    [Column("status")]
    public PendingOnlineMatchStatus Status { get; set; }

    /// <summary>
    /// The scraper source names this row's candidates were searched across - the same list the
    /// bulk search dialog let the user pick, kept so the pending list can show it.
    /// </summary>
    [Required]
    [Column("source_names_json")]
    public string SourceNamesJson { get; set; } = string.Empty;

    /// <summary>
    /// Hand-versioned JSON payload holding the candidate results; see
    /// Services.PendingOnlineMatchPayload. Empty (an empty array) when the search returned no
    /// candidates - the row still exists so the book stays visible under Pending until the user
    /// rejects it.
    /// </summary>
    [Required]
    [Column("results_json")]
    public string ResultsJson { get; set; } = string.Empty;
}
