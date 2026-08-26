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
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
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

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HardcoverDailyLimitExceededException)
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
