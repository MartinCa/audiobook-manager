using System.ComponentModel.DataAnnotations;

namespace AudiobookManager.Api.Dtos;

public class OrganizeAudiobookDto
{
    [Required] public string BookName { get; set; } = null!;
    public string? Subtitle { get; set; }
    public string? Series { get; set; }
    public string? SeriesPart { get; set; }
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
