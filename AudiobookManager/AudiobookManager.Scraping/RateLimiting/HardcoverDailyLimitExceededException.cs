namespace AudiobookManager.Scraping.RateLimiting;

/// <summary>
/// Thrown when the persisted per-UTC-day Hardcover request budget is exhausted. Unlike the
/// burst/per-minute limiters (which queue and wait), this is not something a background job
/// can usefully wait out - the budget only resets at UTC midnight - so it fails fast and is
/// never retried.
/// </summary>
public class HardcoverDailyLimitExceededException : Exception
{
    public HardcoverDailyLimitExceededException(int dailyLimit)
        : base($"Hardcover daily request limit of {dailyLimit} requests has been reached for today (UTC). Further requests are blocked until the limit resets at UTC midnight.")
    {
        DailyLimit = dailyLimit;
    }

    public int DailyLimit { get; }
}
