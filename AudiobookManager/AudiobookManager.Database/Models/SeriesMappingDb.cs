using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("series_mapping")]
public class SeriesMappingDb
{
    [Key]
    [Column("id")]
    public long Id { get; set; }
    [Required]
    [Column("regex")]
    public string Regex { get; set; }
    [Required]
    [Column("mapped_series")]
    public string MappedSeries { get; set; }
    [Required]
    [Column("warn_about_part")]
    public bool WarnAboutPart { get; set; }

    public SeriesMappingDb(long id, string regex, string mappedSeries, bool warnAboutPart)
    {
        Id = id;
        Regex = regex;
        MappedSeries = mappedSeries;
        WarnAboutPart = warnAboutPart;
    }
}
