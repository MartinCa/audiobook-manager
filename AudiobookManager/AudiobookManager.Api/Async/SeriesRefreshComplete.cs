namespace AudiobookManager.Api.Async;

public class SeriesRefreshComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    public SeriesRefreshComplete(int totalProcessed, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}
