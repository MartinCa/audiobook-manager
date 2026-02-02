namespace AudiobookManager.Api.Dtos;

public record AudiobookDetailDto(
    long Id,
    string? BookName,
    string? Subtitle,
    string? Series,
    string? SeriesPart,
    int? Year,
    List<string> Authors,
    List<string> Narrators,
    List<string> Genres,
    string? Description,
    string? Copyright,
    string? Publisher,
    string? Rating,
    string? Asin,
    string? Www,
    string? CoverFilePath,
    int? DurationInSeconds,
    string FilePath,
    string FileName,
    long SizeInBytes
);
