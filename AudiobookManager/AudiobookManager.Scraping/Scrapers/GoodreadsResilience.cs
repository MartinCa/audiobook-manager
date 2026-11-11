using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AudiobookManager.Scraping.Scrapers;

/// <summary>
/// The Goodreads retry pipeline, registered with Microsoft.Extensions.Resilience so scrapers
/// resolve it from <see cref="Polly.Registry.ResiliencePipelineProvider{TKey}"/>. It wraps the whole
/// fetch-and-parse (not just the HTTP call), because a bot-protection challenge or a page with no
/// book data is a thrown exception worth retrying.
/// </summary>
public static class GoodreadsResilience
{
    public const string PipelineName = "goodreads";
    private const int MaxRetries = 5;

    public static IServiceCollection AddGoodreadsResilience(this IServiceCollection services, TimeSpan? retryDelay = null)
    {
        services.AddResiliencePipeline(PipelineName, (builder, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AudiobookManager.Scraping.GoodreadsRetry");
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetries,
                Delay = retryDelay ?? TimeSpan.FromSeconds(3),
                BackoffType = DelayBackoffType.Constant,
                OnRetry = args =>
                {
                    logger.LogWarning("Exception: {exceptionMessage}, Retrying in {sleepDuration}. {attemptNumber}/{maxRetries}", args.Outcome.Exception?.Message, args.RetryDelay, args.AttemptNumber, MaxRetries);
                    return ValueTask.CompletedTask;
                }
            });
        });
        return services;
    }
}
