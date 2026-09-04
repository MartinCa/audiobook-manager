using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AudiobookManager.Scraping.RateLimiting;

/// <summary>
/// Transient-failure retry for the Hardcover API, mirroring the Polly usage in
/// GoodreadsScraper. It is registered *outside* <see cref="HardcoverRateLimitingHandler"/> so
/// every retry attempt travels back through the rate limiter and is counted and throttled
/// again - a retry that bypassed the limiter would defeat the whole point of having one.
///
/// <see cref="HardcoverDailyLimitExceededException"/> is deliberately not handled: the daily
/// budget is exhausted, retrying only burns time.
/// </summary>
public class HardcoverRetryHandler : DelegatingHandler
{
    private const int MaxRetryAttempts = 3;

    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public HardcoverRetryHandler(ILogger<HardcoverRetryHandler> logger)
    {
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome, args.Context.CancellationToken)),
                DelayGenerator = args => ValueTask.FromResult(RetryAfterDelay(args.Outcome)),
                OnRetry = args =>
                {
                    // The failed response is about to be discarded; dispose it so its
                    // connection is not held open by the retry.
                    args.Outcome.Result?.Dispose();
                    logger.LogWarning(
                        "Transient Hardcover API failure ({Status}{Exception}), retrying in {Delay}. {Attempt}/{MaxRetries}",
                        args.Outcome.Result?.StatusCode,
                        args.Outcome.Exception?.Message,
                        args.RetryDelay,
                        args.AttemptNumber + 1,
                        MaxRetryAttempts);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async ct => await base.SendAsync(request, ct),
            cancellationToken);
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome, CancellationToken cancellationToken)
    {
        if (outcome.Exception is HardcoverDailyLimitExceededException)
        {
            return false;
        }

        // The caller gave up - the request was abandoned or the client disconnected. Nothing about
        // that gets better by trying again, and a retry would re-acquire a rate-limit token and
        // spend a unit of the persisted daily budget on work nobody will read.
        //
        // Measured, this does not currently happen: the pipeline checks the token before it
        // retries, so a signalled token stops the retry whatever this predicate answers. But Polly
        // excludes OperationCanceledException from its *default* ShouldHandle, and the clause below
        // overrides that default by naming TaskCanceledException as retryable - so as written this
        // method claims the opposite of what the pipeline does, and the correct behaviour rests on
        // an ordering neither the code nor Polly's retry documentation states. Saying it here costs
        // one branch.
        //
        // It has to stay distinct from an HttpClient *timeout*, which surfaces as the same
        // exception type and is genuinely worth retrying. The token separates them: a timeout
        // leaves it unsignalled.
        if (cancellationToken.IsCancellationRequested && outcome.Exception is OperationCanceledException)
        {
            return false;
        }

        if (outcome.Exception is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            return true;
        }

        var status = outcome.Result?.StatusCode;
        return status == HttpStatusCode.TooManyRequests || (int?)status >= 500;
    }

    /// <summary>
    /// Honors a Retry-After header when the API sends one on a 429, otherwise falls back to
    /// the pipeline's exponential backoff (returning null defers to it).
    /// </summary>
    private static TimeSpan? RetryAfterDelay(Outcome<HttpResponseMessage> outcome)
    {
        var retryAfter = outcome.Result?.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }
}
