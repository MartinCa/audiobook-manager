using AudiobookManager.Services;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Async;

/// <summary>
/// The outcome of one single-book operation that was accepted with a 202 and ran in the background
/// (<see cref="SingleBookOperations.PendingMatchSelect"/>, <see cref="SingleBookOperations.PendingRefreshApply"/>).
/// One event shape for all of them so a row follows its own book by <see cref="AudiobookId"/> and
/// <see cref="Operation"/>; the page that started the work listens for it, because the row that
/// started it collapses (and unmounts) the moment the request is accepted.
/// </summary>
/// <param name="Status">One of <see cref="SingleBookOperationStatus"/>.</param>
/// <param name="Message">Safe to show a user; null for a plain success.</param>
public record SingleBookOperationComplete(long AudiobookId, string Operation, string Status, string? Message);

public static class SingleBookOperations
{
    /// <summary>Fetch the chosen candidate of a pending online match and settle it.</summary>
    public const string PendingMatchSelect = "pending-match-select";

    /// <summary>Apply one book's pending metadata-refresh snapshot from the metadata-refresh page.</summary>
    public const string PendingRefreshApply = "pending-refresh-apply";
}

public static class SingleBookOperationStatus
{
    /// <summary>The work ran to the end (what the apply rules accepted is applied; the rest is pending review).</summary>
    public const string Completed = "completed";

    /// <summary>There was nothing to do: the book's pending snapshot is already gone.</summary>
    public const string NothingToApply = "nothingToApply";

    /// <summary>A different file already sits where the apply would file the book.</summary>
    public const string TargetExists = "targetExists";

    /// <summary>The operation failed; <c>Message</c> says why.</summary>
    public const string Failed = "failed";
}

public static class SingleBookOperationFailures
{
    /// <summary>
    /// The text a failed single-book operation reports. Exceptions whose message is written for the
    /// user (a busy book, an exhausted Hardcover budget, a bad request) are relayed, the same set
    /// the synchronous endpoints used to return as 4xx. Anything else is logged with its context
    /// and reported as a fixed sentence: the exception text routinely contains absolute container
    /// paths, and a SignalR broadcast reaches every connected client.
    /// </summary>
    public static string Describe(Exception ex, ILogger logger, long audiobookId, string doing)
    {
        switch (ex)
        {
            case AudiobookBusyException:
            case Scraping.RateLimiting.HardcoverDailyLimitExceededException:
            case KeyNotFoundException:
            case ArgumentOutOfRangeException:
            case InvalidOperationException:
                logger.LogWarning(ex, "Failed {Doing} for audiobook {AudiobookId}", doing, audiobookId);
                return ex.Message;
            default:
                logger.LogError(ex, "Failed {Doing} for audiobook {AudiobookId}", doing, audiobookId);
                return $"Something went wrong while {doing}. See the server log for details.";
        }
    }
}

public static class SingleBookOperationHubExtensions
{
    /// <summary>
    /// Broadcasts the outcome. A hub failure is logged and swallowed: the work is already done, and
    /// this runs in a fire-and-forget task where an exception would be unobserved.
    /// </summary>
    public static async Task NotifyAsync(
        this IHubContext<OrganizeHub, IOrganize> hub, SingleBookOperationComplete outcome, ILogger logger)
    {
        try
        {
            await hub.Clients.All.SingleBookOperationComplete(outcome);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send {Operation} completion for audiobook {AudiobookId} over SignalR", outcome.Operation, outcome.AudiobookId);
        }
    }
}
