namespace AudiobookManager.Api.Dtos;

public record SeriesConsistencyIssueDto(
    long Id,
    long SeriesId,
    string SeriesName,
    string ErrorMessage,
    DateTime DetectedAt
);
