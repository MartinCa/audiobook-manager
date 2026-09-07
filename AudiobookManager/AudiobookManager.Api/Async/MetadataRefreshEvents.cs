namespace AudiobookManager.Api.Async;

public class MetadataRefreshProgress
{
    public int Processed { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }

    public MetadataRefreshProgress(int processed, int total, int succeeded, int failed)
    {
        Processed = processed;
        Total = total;
        Succeeded = succeeded;
        Failed = failed;
    }
}

public class MetadataRefreshComplete
{
    public int TotalProcessed { get; set; }
    public int Total { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    /// <summary>
    /// Set only when the batch stopped early (currently: the Hardcover daily request budget
    /// was exhausted) rather than running to completion over every eligible book.
    /// </summary>
    public string? StopReason { get; set; }

    public MetadataRefreshComplete(int totalProcessed, int total, int totalSucceeded, int totalFailed, string? stopReason = null)
    {
        TotalProcessed = totalProcessed;
        Total = total;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
        StopReason = stopReason;
    }
}