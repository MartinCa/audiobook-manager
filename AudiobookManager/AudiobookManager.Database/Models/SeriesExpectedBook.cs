using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One entry of a matched series' roster as reported by the metadata source. Owned books
/// are matched against these rows to compute what is missing; <see cref="IsIgnored"/>
/// lets a user dismiss an entry they never intend to own.
/// </summary>
[Table("series_expected_books")]
public class SeriesExpectedBook
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("series_id")]
    public long SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    /// <summary>
    /// Position within the series, kept as a string to match <see cref="Audiobook.SeriesPart"/>
    /// (which allows values like "1.5").
    /// </summary>
    [Column("position")]
    public string? Position { get; set; }

    [Required]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("year")]
    public int? Year { get; set; }

    [Column("source_url")]
    public string? SourceUrl { get; set; }

    [Required]
    [Column("is_ignored")]
    public bool IsIgnored { get; set; }
}
