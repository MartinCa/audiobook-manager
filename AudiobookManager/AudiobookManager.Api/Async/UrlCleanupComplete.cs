namespace AudiobookManager.Api.Async;

public class UrlCleanupComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    public UrlCleanupComplete(int totalProcessed, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}