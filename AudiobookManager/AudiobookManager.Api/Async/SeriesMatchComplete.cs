namespace AudiobookManager.Api.Async;

public class SeriesMatchComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    public SeriesMatchComplete(int totalProcessed, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}
