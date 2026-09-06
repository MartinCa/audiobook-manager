namespace AudiobookManager.Domain;

public class Audiobook
{
    public long? Id { get; set; }
    public List<Person> Authors { get; set; }
    public List<Person> Narrators { get; set; }
    public string? BookName { get; set; }
    public string? Subtitle { get; set; }
    public string? Series { get; set; }
    public string? SeriesPart { get; set; }
    public int? Year { get; set; }
    public List<string> Genres { get; set; }
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public string? Rating { get; set; }
    public string? Asin { get; set; }
    public string? Www { get; set; }
    public AudiobookImage? Cover { get; set; }
    public string? CoverFilePath { get; set; }

    public int? DurationInSeconds { get; set; }
    public bool? ReplaceExisting { get; set; }

    /// <summary>
    /// UTC timestamp of the last time this book's metadata was applied/checked against an online
    /// source. Set only by AudiobookService.MarkMetadataRefreshedAsync - never from request DTOs
    /// and never written by the normal insert/update pipeline (it stays null there).
    /// </summary>
    public DateTime? LastMetadataRefreshedAt { get; set; }

    public AudiobookFileInfo FileInfo { get; set; }

    public Audiobook(List<Person> authors, string? bookName, int? year, AudiobookFileInfo fileInfo)
    {
        Authors = authors;
        BookName = bookName;
        Year = year;
        FileInfo = fileInfo;

        Narrators = new List<Person>();
        Genres = new List<string>();
    }
}
