using System.Threading.RateLimiting;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Scraping.RateLimiting;

/// <summary>
/// Singleton holder of the token bucket guarding the Hardcover API's burst and per-minute
/// limits. The rate-limiting DelegatingHandler is pooled/rebuilt by IHttpClientFactory, so
/// the mutable limiter state has to live here rather than on the handler instance.
///
/// Safety invariant: the bucket holds at most <c>capacity</c> tokens and is replenished at
/// <c>tokensPerMinute</c>, so the most requests that can be issued in any rolling 60s window
/// is capacity + tokensPerMinute. That sum must stay at or under Hardcover's documented
/// hard ceiling of 60 requests/minute, and capacity must stay at or under its documented
/// burst of 10. Both are validated in the constructor so a misconfiguration fails fast at
/// startup instead of silently exceeding the API's limits.
/// </summary>
public sealed class HardcoverRateLimiter : IDisposable
{
    /// <summary>Hardcover's documented hard per-minute ceiling (Free plan).</summary>
    public const int AbsolutePerMinuteCeiling = 60;

    /// <summary>Hardcover's documented burst allowance (Free plan).</summary>
    public const int AbsoluteBurstCeiling = 10;

    /// <summary>Bounded queue so a runaway caller cannot pile up unbounded waiters.</summary>
    public const int QueueLimit = 200;

    private readonly TokenBucketRateLimiter _limiter;

    public HardcoverRateLimiter(IOptions<AudiobookManagerSettings> settings)
        : this(settings.Value.HardcoverBurstLimit, settings.Value.HardcoverPerMinuteLimit)
    {
    }

    public HardcoverRateLimiter(int burstLimit, int perMinuteLimit)
    {
        _limiter = new TokenBucketRateLimiter(CreateOptions(burstLimit, perMinuteLimit));
        Capacity = burstLimit;
        TokensPerMinute = perMinuteLimit;
    }

    public int Capacity { get; }

    public int TokensPerMinute { get; }

    /// <summary>
    /// Builds - and validates - the token bucket options. Public so the invariant can be
    /// asserted directly in tests without spinning up replenishment timers.
    /// </summary>
    public static TokenBucketRateLimiterOptions CreateOptions(int burstLimit, int perMinuteLimit)
    {
        if (burstLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(burstLimit), burstLimit, "Hardcover burst limit must be at least 1.");
        }

        if (perMinuteLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(perMinuteLimit), perMinuteLimit, "Hardcover per-minute limit must be at least 1.");
        }

        if (burstLimit > AbsoluteBurstCeiling)
        {
            throw new ArgumentOutOfRangeException(nameof(burstLimit), burstLimit,
                $"Hardcover burst limit must not exceed the API's documented burst of {AbsoluteBurstCeiling}.");
        }

        if (burstLimit + perMinuteLimit > AbsolutePerMinuteCeiling)
        {
            throw new ArgumentOutOfRangeException(nameof(perMinuteLimit), perMinuteLimit,
                $"Hardcover burst limit ({burstLimit}) plus per-minute limit ({perMinuteLimit}) must not exceed the API's documented ceiling of {AbsolutePerMinuteCeiling} requests per minute.");
        }

        // One token per period, with the period rounded up so the realised rate is at or
        // just under the configured tokens/minute (never above it).
        var periodMs = (int)Math.Ceiling(60_000d / perMinuteLimit);

        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = burstLimit,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(periodMs),
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = QueueLimit,
        };
    }

    /// <summary>
    /// Waits for a token. Waiting is fine here - every caller is a background job - but the
    /// queue is bounded, so a full queue throws rather than blocking forever.
    /// </summary>
    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(1, cancellationToken);

        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException(
                $"Hardcover rate limiter queue is full ({QueueLimit} waiting requests); refusing to queue another request.");
        }
    }

    public void Dispose() => _limiter.Dispose();
}
