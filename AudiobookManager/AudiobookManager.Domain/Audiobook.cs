namespace AudiobookManager.Domain;

public class Audiobook
{
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
    public string? Rating { get; set; }
    public string? Asin { get; set; }
    public string? Www { get; set; }
    public AudiobookImage? Cover { get; set; }

    public int? DurationInSeconds { get; set; }

    public AudiobookFileInfo? FileInfo { get; set; }

    public Audiobook(List<Person> authors, string? bookName, int? year)
    {
        Authors = authors;
        BookName = bookName;
        Year = year;

        Narrators = new List<Person>();
        Genres = new List<string>();
    }
}
