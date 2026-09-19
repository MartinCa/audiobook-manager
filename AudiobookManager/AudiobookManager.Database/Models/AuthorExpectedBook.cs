using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One entry of a matched author's standalone-books roster, as reported by the metadata source -
/// the author-roster counterpart of <see cref="SeriesExpectedBook"/>. Only covers books that do
/// NOT belong to any series: a series' books are already rostered (and refreshed) through that
/// series' own <see cref="SeriesExpectedBook"/> roster and attributed to its authors there, so
/// this roster would double-count them if it didn't filter them out (see
/// AudiobookManager/UPCOMING_RELEASES_DESIGN.md). There is no <c>Position</c>/<c>IsCompilation</c>
/// here - those are series-roster-only concepts a standalone book has no use for.
/// </summary>
[Table("author_expected_books")]
public class AuthorExpectedBook
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
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("year")]
    public int? Year { get; set; }

    /// <summary>A precise release date, when the source reports one - see <see cref="SeriesExpectedBook.ReleaseDate"/>.</summary>
    [Column("release_date")]
    public DateOnly? ReleaseDate { get; set; }

    [Column("source_url")]
    public string? SourceUrl { get; set; }

    [Required]
    [Column("is_ignored")]
    public bool IsIgnored { get; set; }
}
