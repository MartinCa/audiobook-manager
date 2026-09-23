namespace AudiobookManager.Api.Dtos;

public record AuthorConsistencyIssueDto(
    long Id,
    long PersonId,
    string AuthorName,
    string ErrorMessage,
    DateTime DetectedAt
);
