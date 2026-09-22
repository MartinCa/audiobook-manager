using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// Marks two candidate values within one similar-value kind ("authors"/"series", matching
/// <c>SimilarValueService.AuthorGroupsKind</c>/<c>SeriesGroupsKind</c>) as explicitly not
/// similar, so <see cref="AudiobookManager.Services.Similarity.SimilarityGrouper"/> stops
/// unioning them directly. <see cref="ValueA"/>/<see cref="ValueB"/> are always stored ordered
/// by <see cref="StringComparer.Ordinal"/> (A &lt;= B) so a pair is unordered for lookup - a
/// caller never needs to check both directions.
/// </summary>
[Table("ignored_similar_value_pairs")]
public class IgnoredSimilarValuePair
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("kind")]
    [MaxLength(16)]
    public string Kind { get; set; } = string.Empty;

    [Required]
    [Column("value_a")]
    public string ValueA { get; set; } = string.Empty;

    [Required]
    [Column("value_b")]
    public string ValueB { get; set; } = string.Empty;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
