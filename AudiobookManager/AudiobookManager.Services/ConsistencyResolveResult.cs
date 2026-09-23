using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public record BookConsistencyResolveResult(
    long IssueId,
    BookConsistencyIssueType IssueType,
    string ActionTaken,
    string Message
);
