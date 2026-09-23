namespace AudiobookManager.Api.Dtos;

public record BookConsistencyResolveResultDto(
    long IssueId,
    string IssueType,
    string ActionTaken,
    string Message
);
