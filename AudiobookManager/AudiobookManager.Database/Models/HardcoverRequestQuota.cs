using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// Persisted daily request counter for the Hardcover API. The daily budget has to survive a
/// process restart - an in-memory counter would reset mid-day and let the app blow through
/// the documented 5,000 requests/day limit.
/// </summary>
[Table("hardcover_request_quota")]
public class HardcoverRequestQuota
{
    /// <summary>
    /// The UTC day the counter belongs to. Hardcover does not document its reset time; UTC
    /// midnight is assumed (and is the conservative choice for a UTC-hosted container).
    /// </summary>
    [Key]
    [Column("utc_date")]
    public DateOnly UtcDate { get; set; }

    [Column("request_count")]
    public int RequestCount { get; set; }
}
