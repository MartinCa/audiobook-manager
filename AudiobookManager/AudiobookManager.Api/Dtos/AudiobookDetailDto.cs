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
    string? Language,
    string? Rating,
    string? Asin,
    string? Www,
    string? CoverFilePath,
    int? DurationInSeconds,
    string FilePath,
    string FileName,
    long SizeInBytes,
    DateTime? LastMetadataRefreshedAt,
    List<AudiobookAuthorDto> AuthorRefs,
    /// <summary>Keys of the book's qualifiers (abridged, dramatized, ...), canonical order. <c>BookName</c> and <c>Series</c> are the clean values.</summary>
    List<string> Qualifiers
);
