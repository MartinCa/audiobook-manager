namespace AudiobookManager.Api.Dtos;

public record AudiobookSummaryDto(
    long Id,
    string? BookName,
    string? Subtitle,
    string? Series,
    string? SeriesPart,
    int? Year,
    List<string> Authors,
    List<string> Narrators,
    List<string> Genres,
    string? CoverFilePath,
    int? DurationInSeconds
);
