using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One rule of the per-source book-qualifier mapping (see <c>AudiobookManager.Domain.QualifierIndicators</c>):
/// a bracketed <see cref="Indicator"/> in a title scraped from <see cref="Source"/> stands for the
/// qualifier <see cref="QualifierKey"/>. <see cref="Indicator"/> is stored normalized (no
/// surrounding brackets, single spaces) and is unique per source ignoring case.
/// </summary>
[Table("qualifier_indicator")]
public class QualifierIndicator
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("source")]
    public string Source { get; set; }

    [Required]
    [Column("indicator")]
    public string Indicator { get; set; }

    [Required]
    [Column("qualifier_key")]
    public string QualifierKey { get; set; }

    public QualifierIndicator(long id, string source, string indicator, string qualifierKey)
    {
        Id = id;
        Source = source;
        Indicator = indicator;
        QualifierKey = qualifierKey;
    }
}
