using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>
/// Shared "process each item, tolerate per-item failure, report progress" loop used by the
/// library scan, similar-value alignment, and consistency-resolve bulk operations. Failures in
/// one item are logged and counted rather than aborting the rest of the batch.
/// </summary>
public static class BulkOperationRunner
{
    public static async Task<(int Processed, int Succeeded, int Failed)> RunAsync<T>(
        IReadOnlyList<T> items,
        Func<T, Task> action,
        ILogger logger,
        Func<T, string> describeFailure,
        Func<int, int, int, int, Task>? progressAction = null)
    {
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = items.Count;

        foreach (var item in items)
        {
            processed++;

            try
            {
                await action(item);
                succeeded++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Message}", describeFailure(item));
                failed++;
            }

            if (progressAction != null)
            {
                await progressAction(processed, total, succeeded, failed);
            }
        }

        return (processed, succeeded, failed);
    }
}
