using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Scraping.RateLimiting;

/// <summary>
/// Enforces Hardcover's API limits for every request made through the "hardcover" named
/// HttpClient - the single choke point every call site (Search, GetBookDetails, SearchSeries,
/// GetSeriesBooks) goes through, so no caller can opt out.
///
/// Two layers:
/// - burst / per-minute, via the shared <see cref="HardcoverRateLimiter"/> token bucket: the
///   request waits for a token (background work, waiting is fine).
/// - daily budget, via a persisted per-UTC-day counter: checked and incremented *before* the
///   request goes out, and fails fast with <see cref="HardcoverDailyLimitExceededException"/>
///   because waiting hours for a UTC day rollover inside an HTTP call is useless.
/// </summary>
public class HardcoverRateLimitingHandler : DelegatingHandler
{
    private readonly HardcoverRateLimiter _rateLimiter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HardcoverRateLimitingHandler> _logger;
    private readonly AudiobookManagerSettings _settings;

    public HardcoverRateLimitingHandler(
        HardcoverRateLimiter rateLimiter,
        IServiceScopeFactory scopeFactory,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<HardcoverRateLimitingHandler> logger)
    {
        _rateLimiter = rateLimiter;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Daily budget first: no point queueing for a token we are not allowed to spend.
        await ConsumeDailyBudgetAsync(cancellationToken);

        await _rateLimiter.AcquireAsync(cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task ConsumeDailyBudgetAsync(CancellationToken cancellationToken)
    {
        var dailyLimit = _settings.HardcoverDailyRequestLimit;
        if (dailyLimit <= 0)
        {
            throw new HardcoverDailyLimitExceededException(dailyLimit);
        }

        // The handler is pooled by IHttpClientFactory and cannot hold a scoped
        // DatabaseContext, so a short-lived scope is opened per request. At <= 60
        // requests/minute the extra round trip is negligible.
        using var scope = _scopeFactory.CreateScope();
        var quotaRepository = scope.ServiceProvider.GetRequiredService<IHardcoverQuotaRepository>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var consumed = await quotaRepository.TryConsumeAsync(today, dailyLimit);

        cancellationToken.ThrowIfCancellationRequested();

        if (!consumed)
        {
            _logger.LogWarning("Hardcover daily request limit of {DailyLimit} reached for {UtcDate}", dailyLimit, today);
            throw new HardcoverDailyLimitExceededException(dailyLimit);
        }
    }
}
