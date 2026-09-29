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
    DateTime DetectedAt,
    /// <summary>Keys of the book's qualifiers, so the issue list shows the same badges as every other book list.</summary>
    List<string>? Qualifiers = null
);
