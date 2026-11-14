using System.Text.Json.Serialization;

namespace AudiobookManager.Domain;

public class Audiobook
{
    public long? Id { get; set; }
    public List<Person> Authors { get; set; }

    /// <summary>
    /// The name of the one author that stands for the book, or null when it has no single main
    /// author (optional: anthologies and co-authored books often have none). It is always one of
    /// <see cref="Authors"/> (matched by exact name); a value that is not is ignored and dropped on
    /// save. Used alone by the default and automated search query, and - when the library setting
    /// is on - alone in the author folder of the path. Database-only (no m4b tag): carried through
    /// every FromDb -> UpdateAudiobook round trip like <see cref="Qualifiers"/>.
    /// </summary>
    public string? MainAuthor { get; set; }

    /// <summary>
    /// <see cref="MainAuthor"/> if it names one of <see cref="Authors"/>, otherwise null. Everything
    /// that acts on the main author reads this, never the raw property, so a stale name (the author
    /// was removed or renamed) can never reach a path or a query.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveMainAuthor =>
        string.IsNullOrWhiteSpace(MainAuthor) ? null
        : Authors?.FirstOrDefault(a => string.Equals(a.Name, MainAuthor, StringComparison.Ordinal))?.Name;
    public List<Person> Narrators { get; set; }
    public string? BookName { get; set; }
    public string? Subtitle { get; set; }

    /// <summary>
    /// The book's title is kept split at its first ": " into <see cref="BookName"/> and
    /// <see cref="Subtitle"/>; a metadata refresh then splits the source's title the same way.
    /// Database-only bookkeeping (never reaches a tag or the library path), carried through every
    /// FromDb -> UpdateAudiobook round trip like <see cref="Qualifiers"/>.
    /// </summary>
    public bool SplitTitleOnColon { get; set; }
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
