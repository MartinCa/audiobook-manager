namespace AudiobookManager.Api.Async;

public class PendingOnlineMatchSearchProgress
{
    public int Processed { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }

    public PendingOnlineMatchSearchProgress(int processed, int total, int succeeded, int failed)
    {
        Processed = processed;
        Total = total;
        Succeeded = succeeded;
        Failed = failed;
    }
}

public class PendingOnlineMatchSearchComplete
{
    public int TotalProcessed { get; set; }
    public int Total { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    public PendingOnlineMatchSearchComplete(int totalProcessed, int total, int totalSucceeded, int totalFailed)
    {
        TotalProcessed = totalProcessed;
        Total = total;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
    }
}
