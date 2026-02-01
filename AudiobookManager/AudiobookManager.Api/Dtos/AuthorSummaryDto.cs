namespace AudiobookManager.Api.Dtos;

public record AuthorSummaryDto(
    long Id,
    string Name,
    int BookCount
);
