namespace AudiobookManager.Api.Dtos;

public record AuthorDetailDto(
    AuthorSummaryDto Author,
    List<SeriesInfo> Series,
    List<AudiobookSummaryDto> StandaloneBooks
);

public record SeriesInfo(
    string SeriesName,
    int BookCount
);
