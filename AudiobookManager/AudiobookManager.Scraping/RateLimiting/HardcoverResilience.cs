using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace AudiobookManager.Scraping.RateLimiting;

/// <summary>
/// Transient-failure retry for the Hardcover API, built on Microsoft.Extensions.Http.Resilience.
/// It has to be added *before* <see cref="HardcoverRateLimitingHandler"/> (the first handler added
/// is the outermost) so every retry attempt travels back through the rate limiter and is counted
/// and throttled again - a retry that bypassed the limiter would defeat the whole point of having one.
///
/// <see cref="HardcoverDailyLimitExceededException"/> is deliberately not handled: the daily
/// budget is exhausted, retrying only burns time.
/// </summary>
public static class HardcoverResilience
{
    public const int MaxRetryAttempts = 3;
    public const string PipelineName = "hardcover-retry";

    public static IHttpClientBuilder AddHardcoverRetry(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(PipelineName, (pipeline, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AudiobookManager.Scraping.HardcoverRetry");
            pipeline.AddRetry(CreateRetryOptions(logger));
        });
        return builder;
    }

    public static HttpRetryStrategyOptions CreateRetryOptions(ILogger logger) => new()
    {
        MaxRetryAttempts = MaxRetryAttempts,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),
        // Honors a Retry-After header when the API sends one, otherwise the exponential backoff.
        ShouldRetryAfterHeader = true,
        ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome, args.Context.CancellationToken)),
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
    };

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome, CancellationToken cancellationToken)
    {
        if (outcome.Exception is HardcoverDailyLimitExceededException)
        {
            return false;
        }

        // The caller gave up - the request was abandoned or the client disconnected. A retry would
        // re-acquire a rate-limit token and spend a unit of the persisted daily budget on work
        // nobody will read. It has to stay distinct from an HttpClient *timeout*, which surfaces as
        // the same exception type and is worth retrying: a timeout leaves the token unsignalled.
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
}
