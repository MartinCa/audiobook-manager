using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace AudiobookManager.Database.Models;

/// <summary>
/// The single row of UI-editable library-wide settings. Deliberately a typed row rather than a
/// generic key/value table: every future setting gets a column with real type safety, and the
/// "exactly one row" shape is what makes a missing row a bootstrap case rather than a lookup miss.
/// The id is a fixed 1 (not autoincrement), so the primary key itself enforces the singleton
/// shape - a racing second bootstrap insert fails with a UNIQUE violation instead of silently
/// creating a second row.
/// </summary>
[Table("library_settings")]
public class LibrarySettings
{
    public const long SingletonId = 1;

    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    [Column("initials_spacing")]
    public InitialsSpacing InitialsSpacing { get; set; } = InitialsSpacing.Unspaced;

    /// <summary>
    /// Delay the bulk metadata-refresh loop waits between consecutive source requests. Exists to
    /// be kind to scrapers without a client-side rate limiter (Goodreads/Audible); Hardcover
    /// requests are already throttled at the HttpClient layer and simply never see this delay
    /// fully consumed. Milliseconds, so a single integer covers every sane value.
    /// </summary>
    [Column("metadata_refresh_delay_ms")]
    public int MetadataRefreshDelayMs { get; set; } = 1000;

    public LibrarySettings() { }

    public LibrarySettings(long id, InitialsSpacing initialsSpacing, int metadataRefreshDelayMs = 1000)
    {
        Id = id;
        InitialsSpacing = initialsSpacing;
        MetadataRefreshDelayMs = metadataRefreshDelayMs;
    }
}
