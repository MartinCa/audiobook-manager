using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("audiobooks")]
public class Audiobook
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public List<Person> Authors { get; set; }

    public List<Person> Narrators { get; set; }

    [Required]
    [Column("book_name")]
    public string BookName { get; set; }

    [Column("subtitle")]
    public string? Subtitle { get; set; }

    [Column("series")]
    public string? Series { get; set; }

    [Column("series_part")]
    public string? SeriesPart { get; set; }

    [Required]
    [Column("year")]
    public int Year { get; set; }

    public List<Genre> Genres { get; set; }

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

    [Column("cover_file_path")]
    public string? CoverFilePath { get; set; }

    [Column("duration_in_seconds")]
    public int? DurationInSeconds { get; set; }

    [Column("file_info_full_path")]
    public string FileInfoFullPath { get; set; }

    [Column("file_info_file_name")]
    public string FileInfoFileName { get; set; }

    [Column("file_info_size_in_bytes")]
    public long FileInfoSizeInBytes { get; set; }

    public Audiobook(long id, string bookName, string? subtitle, string? series, string? seriesPart, int year, string? description, string? copyright, string? publisher, string? rating, string? asin, string? www, string? coverFilePath, int? durationInSeconds, string fileInfoFullPath, string fileInfoFileName, long fileInfoSizeInBytes)
    {
        Id = id;
        BookName = bookName;
        Subtitle = subtitle;
        Series = series;
        SeriesPart = seriesPart;
        Year = year;
        Description = description;
        Copyright = copyright;
        Publisher = publisher;
        Rating = rating;
        Asin = asin;
        Www = www;
        CoverFilePath = coverFilePath;
        DurationInSeconds = durationInSeconds;
        FileInfoFullPath = fileInfoFullPath;
        FileInfoFileName = fileInfoFileName;
        FileInfoSizeInBytes = fileInfoSizeInBytes;

        Authors = new List<Person>();
        Narrators = new List<Person>();
        Genres = new List<Genre>();
    }
}
