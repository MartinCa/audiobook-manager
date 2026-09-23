namespace AudiobookManager.Api.Dtos;

public record SeriesConsistencyIssuePageDto(
    List<SeriesConsistencyIssueDto> Items,
    int TotalCount
);
