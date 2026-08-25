using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("orphan_directories")]
public class OrphanDirectory
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("directory_path")]
    public string DirectoryPath { get; set; } = string.Empty;

    [Required]
    [Column("detected_at")]
    public DateTime DetectedAt { get; set; }
}
