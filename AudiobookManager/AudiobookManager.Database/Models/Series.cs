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

    /// <summary>
    /// Whether omnibus/box-set editions from the matched source should be kept on the roster
    /// instead of being filtered out. Defaults to false because most series report these
    /// alongside the individual books even though they aren't a distinct missing book, but some
    /// libraries genuinely own the omnibus rather than the individual entries.
    /// </summary>
    [Column("include_omnibus_editions")]
    public bool IncludeOmnibusEditions { get; set; }

    /// <summary>
    /// The unified expected-book rows this series currently reports, on the shared
    /// <see cref="ExpectedBook"/> table - a book discovered by an author refresh and by a series
    /// refresh is one row (the legacy per-series <c>series_expected_books</c> table is gone).
    /// </summary>
    public List<ExpectedBook> ExpectedBooks { get; set; } = new();

    /// <summary>
    /// The regex mapping patterns that route scraped/embedded series values to this series.
    /// Data-model-wise this is the ownership the Settings page's global mappings table used to
    /// approximate with a free-text target column.
    /// </summary>
    public List<SeriesMapping> Mappings { get; set; } = new();
}
