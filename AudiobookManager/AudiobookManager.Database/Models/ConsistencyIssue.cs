using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("consistency_issues")]
public class ConsistencyIssue
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("audiobook_id")]
    public long AudiobookId { get; set; }

    public Audiobook Audiobook { get; set; } = null!;

    [Required]
    [Column("issue_type")]
    public ConsistencyIssueType IssueType { get; set; }

    [Required]
    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("expected_value")]
    public string? ExpectedValue { get; set; }

    [Column("actual_value")]
    public string? ActualValue { get; set; }

    [Required]
    [Column("detected_at")]
    public DateTime DetectedAt { get; set; }
}
