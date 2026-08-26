namespace AudiobookManager.Api.Dtos;

public record LibraryBookHitDto(
    long Id,
    string? BookName,
    string? Subtitle,
    List<string> Authors,
    string? Series,
    int? Year,
    string? CoverFilePath
);

public record LibraryAuthorHitDto(
    long Id,
    string Name,
    int BookCount
);

public record LibrarySeriesHitDto(
    string Name,
    int BookCount
);

public record LibrarySearchResultDto(
    List<LibraryBookHitDto> Books,
    List<LibraryAuthorHitDto> Authors,
    List<LibrarySeriesHitDto> Series
);
