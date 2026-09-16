namespace AudiobookManager.Api.Async;

/// <summary>Completion of a pending series-refresh apply.</summary>
public class SeriesRefreshApplyComplete
{
    public int TotalProcessed { get; }
    public int TotalSucceeded { get; }
    public int TotalFailed { get; }

    public SeriesRefreshApplyComplete(int totalProcessed, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}