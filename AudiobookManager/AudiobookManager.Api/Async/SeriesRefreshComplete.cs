namespace AudiobookManager.Api.Async;

public class SeriesRefreshComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    /// <summary>
    /// Set only when the batch stopped early (currently: the Hardcover daily request budget
    /// was exhausted) rather than running to completion over every requested series.
    /// </summary>
    public string? StopReason { get; set; }

    public SeriesRefreshComplete(int totalProcessed, int totalSucceeded, int totalFailed, string? stopReason = null)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
        StopReason = stopReason;
    }
}
