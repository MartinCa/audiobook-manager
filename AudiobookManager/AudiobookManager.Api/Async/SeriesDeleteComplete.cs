namespace AudiobookManager.Api.Async;

public class SeriesDeleteComplete
{
    public int TotalProcessed { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }

    /// <summary>
    /// True only when the delete threw out of the background operation (e.g. the catalog row
    /// delete itself failed) and this completion came from <c>BackgroundOperationRunner</c>'s
    /// error path rather than a real result. Every zero-count field there is indistinguishable
    /// from "a series with no owned books, deleted successfully" - this flag is what lets the
    /// client tell the two apart instead of toasting a crashed delete as a success.
    /// </summary>
    public bool Errored { get; set; }

    public SeriesDeleteComplete(int totalProcessed, int totalSucceeded, int totalFailed, bool errored = false)
    {
        TotalProcessed = totalProcessed;
        TotalSucceeded = totalSucceeded;
        TotalFailed = totalFailed;
        Errored = errored;
    }
}
