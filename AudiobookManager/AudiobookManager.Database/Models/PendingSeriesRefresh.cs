using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// The one pending series-refresh snapshot a matched series may hold: the roster and explicit
/// changes a scrape returned for the series' matched source, stored until the user applies or
/// dismisses them. One row per series (only for series whose refresh produced changes - a
/// no-change bulk item never appears here), keyed uniquely by the series name and overwritten by
/// the next refresh.
/// </summary>
[Table("pending_series_refresh")]
public class PendingSeriesRefresh
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>The free-text series value, matching <see cref="Series.Name"/> verbatim.</summary>
    [Required]
    [Column("series_name")]
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the fetch that produced this snapshot.</summary>
    [Required]
    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }

    /// <summary>Scraper source the snapshot came from (e.g. "Hardcover").</summary>
    [Required]
    [Column("source_name")]
    public string SourceName { get; set; } = string.Empty;

    /// <summary>The source series page URL that was fetched.</summary>
    [Required]
    [Column("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Hand-versioned JSON payload; see Services.PendingSeriesRefreshPayload.</summary>
    [Required]
    [Column("payload_json")]
    public string PayloadJson { get; set; } = string.Empty;
}