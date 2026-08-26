using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// Read-side catalog row for a series value found on <see cref="Audiobook.Series"/>.
/// This table never drives what is written onto an audiobook - it only records what an
/// external metadata source (currently Hardcover) says the full roster of the series is,
/// so missing books can be reported.
/// </summary>
[Table("series")]
public class Series
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// The free-text series value, matching <see cref="Audiobook.Series"/> verbatim.
    /// </summary>
    [Required]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("matched_source_name")]
    public string? MatchedSourceName { get; set; }

    [Column("matched_source_id")]
    public string? MatchedSourceId { get; set; }

    [Column("matched_source_url")]
    public string? MatchedSourceUrl { get; set; }

    [Column("matched_series_name")]
    public string? MatchedSeriesName { get; set; }

    [Column("match_confidence")]
    public double? MatchConfidence { get; set; }

    [Column("last_refreshed_at")]
    public DateTime? LastRefreshedAt { get; set; }

    public List<SeriesExpectedBook> ExpectedBooks { get; set; } = new();
}
