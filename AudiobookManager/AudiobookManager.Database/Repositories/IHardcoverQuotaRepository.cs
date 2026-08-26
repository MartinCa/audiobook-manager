namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Persisted per-UTC-day request budget for the Hardcover API.
/// </summary>
public interface IHardcoverQuotaRepository
{
    /// <summary>
    /// Atomically consumes one request from the given UTC day's budget. Returns false (and
    /// consumes nothing) when the day's count already reached <paramref name="dailyLimit"/>.
    /// </summary>
    Task<bool> TryConsumeAsync(DateOnly utcDate, int dailyLimit);

    Task<int> GetCountAsync(DateOnly utcDate);
}
