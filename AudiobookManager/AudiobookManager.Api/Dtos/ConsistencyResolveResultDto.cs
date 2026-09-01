namespace AudiobookManager.Api.Dtos;

public record ConsistencyResolveResultDto(
    long IssueId,
    string IssueType,
    string ActionTaken,
    string Message
);
