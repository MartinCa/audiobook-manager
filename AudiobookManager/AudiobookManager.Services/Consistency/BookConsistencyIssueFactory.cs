using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

/// <summary>
/// Shared constructor for the detectors so <see cref="BookConsistencyIssue.DetectedAt"/> is stamped
/// consistently.
///
/// Leaves <see cref="BookConsistencyIssue.Audiobook"/> null - fine for the insert path this is meant
/// for, but it means an issue built here must never be handed to an
/// <see cref="IBookConsistencyIssueResolver"/>, all of which read <c>issue.Audiobook</c>. Nothing in
/// the type system enforces that; it holds only because the repository methods resolvers are fed
/// from (<c>GetByTypeAsync</c>, <c>GetByIdsAsync</c>, <c>GetByIdAsync</c>) load the graph
/// themselves rather than returning what was inserted.
/// </summary>
public static class BookConsistencyIssueFactory
{
    public static BookConsistencyIssue Create(long audiobookId, BookConsistencyIssueType issueType, string description, string? expectedValue, string? actualValue) =>
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
