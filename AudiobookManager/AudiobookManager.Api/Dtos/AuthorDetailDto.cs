namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One author's detail: the summary plus one page of each of the two sections. The sections were
/// whole lists of an author's entire catalogue; they are paged now so a heavy author cannot send
/// every series and standalone book over the wire and into the DOM at once.
/// </summary>
public record AuthorDetailDto(
    AuthorSummaryDto Author,
    PaginatedResult<SeriesInfo> Series,
    PaginatedResult<AudiobookSummaryDto> StandaloneBooks
);

public record SeriesInfo(
    string SeriesName,
    int BookCount
);
