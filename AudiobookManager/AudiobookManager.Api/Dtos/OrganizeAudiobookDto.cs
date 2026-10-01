using System.ComponentModel.DataAnnotations;

namespace AudiobookManager.Api.Dtos;

public class OrganizeAudiobookDto
{
    [Required] public string BookName { get; set; } = null!;
    public string? Subtitle { get; set; }

    /// <summary>
    /// Records that the title is kept split at its first colon into <c>BookName</c> and
    /// <c>Subtitle</c>, so a metadata refresh splits the source's title the same way. Database-only
    /// bookkeeping - it never reaches a tag or the library path.
    /// </summary>
    public bool SplitTitleOnColon { get; set; }
    public string? Series { get; set; }
    public string? SeriesPart { get; set; }

    /// <summary>
    /// The book's non-primary series (<c>Series</c>/<c>SeriesPart</c> are the primary one, the
    /// only one that reaches the m4b tags and the library path). Null means "not specified" and
    /// leaves the stored ones alone; an empty list removes them.
    /// </summary>
    public List<SeriesRelationDto>? AdditionalSeries { get; set; }

    /// <summary>
    /// Keys of the book's qualifiers (see <c>BookQualifiers</c>). <c>BookName</c> and <c>Series</c>
    /// are the clean values - the suffixes are added when the book is written to disk.
    /// </summary>
    public List<string> Qualifiers { get; set; } = new();
    [Required] public int? Year { get; set; }
    [Required] public List<string> Authors { get; set; } = null!;
    public List<string> Narrators { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public string? Rating { get; set; }
    public string? Asin { get; set; }
    public string? Www { get; set; }
    public OrganizeAudiobookCoverDto? Cover { get; set; }
    [Required] public string FilePath { get; set; } = null!;
    [Required] public string FileName { get; set; } = null!;
    public long SizeInBytes { get; set; }
    public bool ReplaceExisting { get; set; } = false;

    /// <summary>
    /// Client signal, not a tag: "fields on this save were applied from a metadata search result".
    /// The controller maps it onto the domain object; the service stamps the bookkeeping
    /// timestamp after the save succeeds. The timestamp itself is never accepted from clients.
    /// </summary>
    public bool MetadataAppliedFromSearch { get; set; } = false;
}

public class OrganizeAudiobookCoverDto
{
    [Required] public string Base64Data { get; set; } = null!;
    [Required] public string MimeType { get; set; } = null!;
}

public class TargetPathCheckDto
{
    public string TargetPath { get; set; }
    public bool Exists { get; set; }
    public ExistingTargetFileDto? Existing { get; set; }

    public TargetPathCheckDto(Services.TargetPathCollisionResult result)
    {
        TargetPath = result.TargetPath;
        Exists = result.Exists;
        Existing = result.Exists
            ? new ExistingTargetFileDto
            {
                AudiobookId = result.ExistingAudiobookId,
                SizeInBytes = result.ExistingSizeInBytes ?? 0,
                DurationInSeconds = result.ExistingDurationInSeconds
            }
            : null;
    }
}

public class ExistingTargetFileDto
{
    public long? AudiobookId { get; set; }
    public long SizeInBytes { get; set; }
    public int? DurationInSeconds { get; set; }
}
