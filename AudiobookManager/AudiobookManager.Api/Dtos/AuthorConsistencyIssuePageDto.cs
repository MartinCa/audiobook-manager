namespace AudiobookManager.Api.Dtos;

public record AuthorConsistencyIssuePageDto(
    List<AuthorConsistencyIssueDto> Items,
    int TotalCount
);
