namespace AudiobookManager.Api.Async;

public class SeriesRefreshProgress
{
    public int Processed { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }

    public SeriesRefreshProgress(int processed, int total, int succeeded, int failed)
    {
        Processed = processed;
        Total = total;
        Succeeded = succeeded;
        Failed = failed;
    }
}
