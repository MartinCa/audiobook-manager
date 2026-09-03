using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

/// <summary>Shared constructor for the detectors so <see cref="ConsistencyIssue.DetectedAt"/> is stamped consistently.</summary>
public static class ConsistencyIssueFactory
{
    public static ConsistencyIssue Create(long audiobookId, ConsistencyIssueType issueType, string description, string? expectedValue, string? actualValue) =>
        new()
        {
            AudiobookId = audiobookId,
            IssueType = issueType,
            Description = description,
            ExpectedValue = expectedValue,
            ActualValue = actualValue,
            DetectedAt = DateTime.UtcNow
        };
}
