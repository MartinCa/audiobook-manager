namespace AudiobookManager.Api.Async;

/// <summary>Progress of a pending series-refresh apply running in the background.</summary>
public class SeriesRefreshApplyProgress
{
    public int Processed { get; }
    public int Total { get; }
    public int Succeeded { get; }
    public int Failed { get; }

    public SeriesRefreshApplyProgress(int processed, int total, int succeeded, int failed)
    {
        Processed = processed;
        Total = total;
        Succeeded = succeeded;
        Failed = failed;
    }
}