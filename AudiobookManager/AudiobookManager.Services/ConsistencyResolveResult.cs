using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public record ConsistencyResolveResult(
    long IssueId,
    ConsistencyIssueType IssueType,
    string ActionTaken,
    string Message
);
