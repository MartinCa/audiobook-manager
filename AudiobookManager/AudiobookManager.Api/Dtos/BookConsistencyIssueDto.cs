namespace AudiobookManager.Api.Dtos;

public record BookConsistencyIssueDto(
    long Id,
    long AudiobookId,
    string BookName,
    List<string> Authors,
    string IssueType,
    string Description,
    string? ExpectedValue,
    string? ActualValue,
    DateTime DetectedAt
);
