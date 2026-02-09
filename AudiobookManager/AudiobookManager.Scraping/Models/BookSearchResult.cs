using AudiobookManager.Domain;

namespace AudiobookManager.Scraping.Models;
public class BookSearchResult
{
    public string Url { get; set; }
    public IList<Person> Authors { get; set; }
    public IList<Person> Narrators { get; set; }
    public string BookName { get; set; }
    public string? Subtitle { get; set; }
    public string? Duration { get; set; }
    public int? Year { get; set; }
    public string? Language { get; set; }
    public string? ImageUrl { get; set; }
    public IList<BookSeriesSearchResult>? Series { get; set; }
    public string? Description { get; set; }
    public IList<string> Genres { get; set; }
    public float? Rating { get; set; }
    public int? NumberOfRatings { get; set; }
    public string? Copyright { get; set; }
    public string? Publisher { get; set; }
    public string? Asin { get; set; }
    public string? Isbn { get; set; }

    public BookSearchResult(string url, string bookName)
    {
        Url = url;
        BookName = bookName;

        Authors = new List<Person>();
        Narrators = new List<Person>();
        Genres = new List<string>();
    }
}
