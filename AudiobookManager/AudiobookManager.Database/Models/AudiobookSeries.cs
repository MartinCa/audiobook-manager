using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One series a book belongs to, with the book's (optional) part in it. Every book has at most one
/// primary relation, and that one is mirrored onto <see cref="Audiobook.Series"/> /
/// <see cref="Audiobook.SeriesPart"/> - the columns that drive tags, the library path and the
/// book list. Series are matched to the catalog by name, exactly as before, so there is no FK to
/// the series table.
/// </summary>
[Table("audiobook_series")]
public class AudiobookSeries
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Column("audiobook_id")]
    public long AudiobookId { get; set; }

    public Audiobook Audiobook { get; set; } = null!;

    [Required]
    [Column("series_name")]
    public string SeriesName { get; set; } = string.Empty;

    [Column("series_part")]
    public string? SeriesPart { get; set; }

    [Column("is_primary")]
    public bool IsPrimary { get; set; }

    /// <summary>Display order within the book; the primary is always 0.</summary>
    [Column("sort_order")]
    public int SortOrder { get; set; }

    /// <summary>
    /// Accent-folded <see cref="SeriesName"/>, kept in sync by AccentFoldedColumnsInterceptor for
    /// the same reason as Audiobook.SeriesFolded.
    /// </summary>
    [Column("series_name_folded")]
    public string? SeriesNameFolded { get; set; }
}
