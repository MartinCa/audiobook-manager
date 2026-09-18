namespace AudiobookManager.Api.Async;

public class SeriesDeleteComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    public SeriesDeleteComplete(int totalProcessed, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}
