namespace AudiobookManager.Api.Async;

/// <summary>Completion of a pending series-refresh apply.</summary>
public class SeriesRefreshApplyComplete
{
    public int TotalProcessed { get; }
    public int TotalSucceeded { get; }
    public int TotalFailed { get; }

    /// <summary>
    /// The series' effective name after the apply: the adopted source name when the
    /// source-series-name adoption fully succeeded (the client navigates that route there), or
    /// null when no adoption ran, it was a no-op (blank or already the series' own name), or it
    /// failed partially. A null means the series is still addressable under its original name.
    /// </summary>
    public string? EffectiveSeriesName { get; }

    public SeriesRefreshApplyComplete(int totalProcessed, int totalSucceeded, int totalFailed, string? effectiveSeriesName = null)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
        EffectiveSeriesName = effectiveSeriesName;
    }
}