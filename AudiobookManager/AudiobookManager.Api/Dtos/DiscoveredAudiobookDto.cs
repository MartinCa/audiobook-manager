using AudiobookManager.Database.Models;

namespace AudiobookManager.Api.Dtos;

public class DiscoveredAudiobookDto
{
    public string FullPath { get; set; }
    public string FileName { get; set; }
    public long SizeInBytes { get; set; }
    public string BookName { get; set; }
    public string? Subtitle { get; set; }
    public string? Series { get; set; }
    public string? SeriesPart { get; set; }
    public int? Year { get; set; }
    public string? Authors { get; set; }
    public string? Narrators { get; set; }
    public string? Genres { get; set; }
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public string? Rating { get; set; }
    public string? Asin { get; set; }
    public string? Www { get; set; }
    public int? DurationInSeconds { get; set; }
    public bool IsWellTagged { get; set; }
    public bool IsDuplicate { get; set; }

    public DiscoveredAudiobookDto(DiscoveredAudiobook discovered)
    {
        FullPath = discovered.FileInfoFullPath;
        FileName = discovered.FileInfoFileName;
        SizeInBytes = discovered.FileInfoSizeInBytes;
        BookName = discovered.BookName;
        Subtitle = discovered.Subtitle;
        Series = discovered.Series;
        SeriesPart = discovered.SeriesPart;
        Year = discovered.Year;
        Authors = discovered.Authors;
        Narrators = discovered.Narrators;
        Genres = discovered.Genres;
        Description = discovered.Description;
        Copyright = discovered.Copyright;
        Publisher = discovered.Publisher;
        Language = discovered.Language;
        Rating = discovered.Rating;
        Asin = discovered.Asin;
        Www = discovered.Www;
        DurationInSeconds = discovered.DurationInSeconds;
        IsWellTagged = !string.IsNullOrWhiteSpace(discovered.Authors)
            && !string.IsNullOrWhiteSpace(discovered.BookName)
            && discovered.Year.HasValue;
    }
}

public class BulkImportDiscoveredDto
{
    public List<string> Paths { get; set; } = new();
}
