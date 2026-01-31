using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("discovered_audiobooks")]
public class DiscoveredAudiobook
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("book_name")]
    public string BookName { get; set; }

    [Column("subtitle")]
    public string? Subtitle { get; set; }

    [Column("series")]
    public string? Series { get; set; }

    [Column("series_part")]
    public string? SeriesPart { get; set; }

    [Column("year")]
    public int? Year { get; set; }

    [Column("authors")]
    public string? Authors { get; set; }

    [Column("narrators")]
    public string? Narrators { get; set; }

    [Column("genres")]
    public string? Genres { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("copyright")]
    public string? Copyright { get; set; }

    [Column("publisher")]
    public string? Publisher { get; set; }

    [Column("rating")]
    public string? Rating { get; set; }

    [Column("asin")]
    public string? Asin { get; set; }

    [Column("www")]
    public string? Www { get; set; }

    [Column("duration_in_seconds")]
    public int? DurationInSeconds { get; set; }

    [Required]
    [Column("file_info_full_path")]
    public string FileInfoFullPath { get; set; }

    [Required]
    [Column("file_info_file_name")]
    public string FileInfoFileName { get; set; }

    [Column("file_info_size_in_bytes")]
    public long FileInfoSizeInBytes { get; set; }

    [Required]
    [Column("discovered_at")]
    public DateTime DiscoveredAt { get; set; }

    public DiscoveredAudiobook(string bookName, string fileInfoFullPath, string fileInfoFileName, long fileInfoSizeInBytes, DateTime discoveredAt)
    {
        BookName = bookName;
        FileInfoFullPath = fileInfoFullPath;
        FileInfoFileName = fileInfoFileName;
        FileInfoSizeInBytes = fileInfoSizeInBytes;
        DiscoveredAt = discoveredAt;
    }
}
