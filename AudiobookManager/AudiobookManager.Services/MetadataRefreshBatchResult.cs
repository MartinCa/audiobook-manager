namespace AudiobookManager.Services;

/// <summary>
/// The tallies one bulk metadata refresh run reports on completion. Carries the same numbers the
/// per-item progress events reported (so the completion stays consistent with what the user
/// watched), plus the reason the loop stopped early when it did - currently only the Hardcover
/// daily request budget being exhausted - so callers can surface it instead of reporting a plain
/// success/failure summary.
/// </summary>
public record MetadataRefreshBatchResult(
    int Processed,
    int Total,
    int Succeeded,
    int Failed,
    string? StopReason);
