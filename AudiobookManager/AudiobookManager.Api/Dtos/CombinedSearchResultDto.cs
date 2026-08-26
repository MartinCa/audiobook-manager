namespace AudiobookManager.Api.Dtos;

public record BookSearchHitDto(
    long Id,
    string? BookName,
    string? Subtitle,
    List<string> Authors,
    string? Series,
    int? Year,
    string? CoverFilePath
);

public record AuthorSearchHitDto(
    long Id,
    string Name,
    int BookCount
);

public record SeriesSearchHitDto(
    string Name,
    int BookCount
);

public record CombinedSearchResultDto(
    List<BookSearchHitDto> Books,
    List<AuthorSearchHitDto> Authors,
    List<SeriesSearchHitDto> Series
);
