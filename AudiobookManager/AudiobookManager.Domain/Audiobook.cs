using System.Text.Json.Serialization;

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

    /// <summary>
    /// The book's other (non-primary) series, in display order. <see cref="Series"/> and
    /// <see cref="SeriesPart"/> above are the primary one - the only series that reaches the m4b
    /// tags, <c>metadata.opf</c> and the library path - so these are database-only.
    /// <c>null</c> means "not specified: leave the stored relations as they are" (a domain object
    /// parsed from a file, or built by a caller that never read them); an empty list means "this
    /// book has no other series" and removes any that were stored.
    /// </summary>
    public List<SeriesRelation>? AdditionalSeries { get; set; }

    /// <summary>
    /// Keys of the <see cref="BookQualifiers"/> this book carries (canonical order). The
    /// <see cref="BookName"/> and <see cref="Series"/> above are always the clean values; the
    /// suffix only appears in <see cref="EffectiveBookName"/> / <see cref="EffectiveSeries"/>,
    /// which is what every writer to disk uses.
    /// </summary>
    public List<string> Qualifiers { get; set; } = new();

    /// <summary><see cref="BookName"/> with the qualifier suffixes appended - the name on disk.</summary>
    [JsonIgnore]
    public string? EffectiveBookName => BookQualifiers.Apply(BookName, Qualifiers);

    /// <summary><see cref="Series"/> with the qualifier suffixes appended - the series on disk.</summary>
    [JsonIgnore]
    public string? EffectiveSeries => BookQualifiers.Apply(Series, Qualifiers);
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
