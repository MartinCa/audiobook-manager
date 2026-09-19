using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// Marks one <see cref="Series"/> as followed for upcoming releases. One row per series; the
/// worker polls only matched series with a follow row for new releases.
/// </summary>
[Table("series_follows")]
public class SeriesFollow
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("series_id")]
    public long SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
