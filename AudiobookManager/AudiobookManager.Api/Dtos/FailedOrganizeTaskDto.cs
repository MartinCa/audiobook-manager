namespace AudiobookManager.Api.Dtos;

public record FailedOrganizeTaskDto(
    string OriginalFileLocation,
    DateTime QueuedTime,
    int FailureCount,
    string? LastFailureReason,
    DateTime? LastFailureAt);
