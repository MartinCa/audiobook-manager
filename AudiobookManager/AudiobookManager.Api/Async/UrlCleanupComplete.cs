namespace AudiobookManager.Api.Async;

public class UrlCleanupComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    /// <summary>
    /// True only when the sweep threw out of the background operation and this completion came
    /// from <c>BackgroundOperationRunner</c>'s error path rather than a real result. Every
    /// zero-count field there is indistinguishable from "the sweep had nothing left to do" -
    /// this flag is what lets the client tell the two apart instead of toasting a crashed sweep
    /// as a success.
    /// </summary>
    public bool Errored { get; set; }

    public UrlCleanupComplete(int totalProcessed, int totalSucceeded, int totalFailed, bool errored = false)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
        Errored = errored;
    }
}
