using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One upcoming release discovered for a followed author or series. Rows are never deleted by a
/// refresh, even once the release date has passed - only an explicit user removal deletes one
/// (see <see cref="AudiobookManager.Services.IUpcomingReleaseService"/>). <see cref="PersonId"/>
/// and <see cref="SeriesId"/> are both nullable and independently set: a book discovered through
/// a followed author carries only <see cref="PersonId"/> until a later poll of a followed series
/// (or vice versa) also reports it, at which point both are filled in on the same row - rows are
/// deduplicated by (<see cref="SourceName"/>, <see cref="SourceBookId"/>), never inserted twice.
/// </summary>
[Table("upcoming_releases")]
public class UpcomingRelease
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("release_date")]
    public DateOnly ReleaseDate { get; set; }

    [Column("person_id")]
    public long? PersonId { get; set; }

    public Person? Person { get; set; }

    [Column("series_id")]
    public long? SeriesId { get; set; }

    public Series? Series { get; set; }

    [Column("series_position")]
    public string? SeriesPosition { get; set; }

    [Required]
    [Column("source_name")]
    public string SourceName { get; set; } = string.Empty;

    [Required]
    [Column("source_book_id")]
    public string SourceBookId { get; set; } = string.Empty;

    [Column("source_url")]
    public string? SourceUrl { get; set; }

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [Required]
    [Column("discovered_at")]
    public DateTime DiscoveredAt { get; set; }
}
