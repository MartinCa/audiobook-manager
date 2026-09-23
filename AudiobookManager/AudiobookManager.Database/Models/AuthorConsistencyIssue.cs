using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One author's most recent failed roster refresh (single or bulk) - the author-scoped
/// counterpart to <see cref="SeriesConsistencyIssue"/>. At most one row per author: a fresh
/// failure replaces the previous error rather than accumulating a history, and a subsequent
/// successful refresh deletes the row.
/// </summary>
[Table("author_consistency_issues")]
public class AuthorConsistencyIssue
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("person_id")]
    public long PersonId { get; set; }

    public Person Person { get; set; } = null!;

    [Required]
    [Column("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [Required]
    [Column("detected_at")]
    public DateTime DetectedAt { get; set; }
}
