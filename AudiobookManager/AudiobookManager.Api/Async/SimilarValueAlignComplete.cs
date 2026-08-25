namespace AudiobookManager.Api.Async;

public class SimilarValueAlignComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    public SimilarValueAlignComplete(int totalProcessed, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}
