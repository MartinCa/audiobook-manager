using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One regex mapping pattern owned by a <see cref="Series"/>. When a scraped or embedded series
/// value matches <see cref="Regex"/>, it is normalized to the owning series' <see cref="Series.Name"/>
/// - the pattern never carries its own target.
/// </summary>
[Table("series_mapping")]
public class SeriesMapping
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    [Required]
    [Column("regex")]
    public string Regex { get; set; }
    [Required]
    [Column("warn_about_part")]
    public bool WarnAboutPart { get; set; }

    [Column("series_id")]
    public long SeriesId { get; set; }
    public Series? Series { get; set; }

    public SeriesMapping(long id, string regex, bool warnAboutPart, long seriesId = default)
    {
        Id = id;
        Regex = regex;
        WarnAboutPart = warnAboutPart;
        SeriesId = seriesId;
    }
}
