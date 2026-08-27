using System.Net;
using System.Net.Http.Headers;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Scraping.RateLimiting;

[TestClass]
public class HardcoverRateLimitingTests
{
    private Mock<IHardcoverQuotaRepository> _quotaRepository = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    [TestInitialize]
    public void Setup()
    {
        _quotaRepository = new Mock<IHardcoverQuotaRepository>();
        _quotaRepository.Setup(r => r.TryConsumeAsync(It.IsAny<DateOnly>(), It.IsAny<int>())).ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddScoped(_ => _quotaRepository.Object);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static AudiobookManagerSettings Settings(int daily = 5000, int burst = 5, int perMinute = 55) => new()
    {
        HardcoverDailyRequestLimit = daily,
        HardcoverBurstLimit = burst,
        HardcoverPerMinuteLimit = perMinute,
    };

    private HttpClient MakeClient(AudiobookManagerSettings settings, HttpMessageHandler inner)
    {
        var handler = new HardcoverRateLimitingHandler(
            new HardcoverRateLimiter(settings.HardcoverBurstLimit, settings.HardcoverPerMinuteLimit),
            _scopeFactory,
            Options.Create(settings),
            new Mock<ILogger<HardcoverRateLimitingHandler>>().Object)
        {
            InnerHandler = inner,
        };

        return new HttpClient(handler);
    }

    [TestMethod]
    public void CreateOptions_DefaultsStayUnderTheDocumentedPerMinuteCeiling()
    {
        var settings = Settings();
        var options = HardcoverRateLimiter.CreateOptions(settings.HardcoverBurstLimit, settings.HardcoverPerMinuteLimit);

        // Worst case in any rolling minute is a full bucket plus a minute's worth of refill.
        var refillPerMinute = TimeSpan.FromMinutes(1).TotalMilliseconds / options.ReplenishmentPeriod.TotalMilliseconds * options.TokensPerPeriod;

        Assert.AreEqual(settings.HardcoverBurstLimit, options.TokenLimit);
        Assert.IsTrue(options.TokenLimit <= HardcoverRateLimiter.AbsoluteBurstCeiling);
        Assert.IsTrue(
            refillPerMinute <= settings.HardcoverPerMinuteLimit,
            $"refill rate {refillPerMinute}/min exceeded the configured {settings.HardcoverPerMinuteLimit}/min");
        Assert.IsTrue(
            options.TokenLimit + refillPerMinute <= HardcoverRateLimiter.AbsolutePerMinuteCeiling,
            $"burst+refill {options.TokenLimit + refillPerMinute} exceeded the API ceiling of {HardcoverRateLimiter.AbsolutePerMinuteCeiling}");
        Assert.IsTrue(options.QueueLimit > 0);
        Assert.AreEqual(System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst, options.QueueProcessingOrder);
    }

    [TestMethod]
    public void CreateOptions_ThrowsWhenBurstPlusPerMinuteWouldExceedTheApiCeiling()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HardcoverRateLimiter.CreateOptions(10, 60));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HardcoverRateLimiter.CreateOptions(20, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HardcoverRateLimiter.CreateOptions(0, 10));
    }

    [TestMethod]
    public async Task Handler_LetsARequestUnderTheLimitsThrough()
    {
        var inner = new StubHandler(HttpStatusCode.OK);
        using var client = MakeClient(Settings(), inner);

        var response = await client.GetAsync("https://api.hardcover.app/v1/graphql");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, inner.CallCount);
        _quotaRepository.Verify(r => r.TryConsumeAsync(DateOnly.FromDateTime(DateTime.UtcNow), 5000), Times.Once);
    }

    [TestMethod]
    public async Task Handler_ThrowsAndSendsNothingWhenTheDailyLimitIsReached()
    {
        _quotaRepository.Setup(r => r.TryConsumeAsync(It.IsAny<DateOnly>(), It.IsAny<int>())).ReturnsAsync(false);

        var inner = new StubHandler(HttpStatusCode.OK);
        using var client = MakeClient(Settings(daily: 3), inner);

        var ex = await Assert.ThrowsExactlyAsync<HardcoverDailyLimitExceededException>(
            () => client.GetAsync("https://api.hardcover.app/v1/graphql"));

        Assert.AreEqual(3, ex.DailyLimit);
        Assert.AreEqual(0, inner.CallCount, "no request may reach the API once the daily budget is spent");
    }

    [TestMethod]
    public async Task Handler_StopsExactlyAtTheConfiguredDailyThreshold()
    {
        // Real counting behaviour, in-memory: the third request of a limit-2 day is refused.
        var counts = new Dictionary<DateOnly, int>();
        _quotaRepository
            .Setup(r => r.TryConsumeAsync(It.IsAny<DateOnly>(), It.IsAny<int>()))
            .ReturnsAsync((DateOnly date, int limit) =>
            {
                counts.TryGetValue(date, out var current);
                if (current >= limit)
                {
                    return false;
                }

                counts[date] = current + 1;
                return true;
            });

        var inner = new StubHandler(HttpStatusCode.OK);
        using var client = MakeClient(Settings(daily: 2), inner);

        await client.GetAsync("https://api.hardcover.app/v1/graphql");
        await client.GetAsync("https://api.hardcover.app/v1/graphql");

        await Assert.ThrowsExactlyAsync<HardcoverDailyLimitExceededException>(
            () => client.GetAsync("https://api.hardcover.app/v1/graphql"));

        Assert.AreEqual(2, inner.CallCount);
    }

    [TestMethod]
    public void CreateOptions_ThrowsWhenConfiguredLimitsWouldExceedTheHard60PerMinuteCeiling()
    {
        // Mirrors the settings-driven constructor path: a misconfigured
        // AudiobookManagerSettings (burst+perMinute > 60) must fail fast at startup rather
        // than silently exceeding Hardcover's documented ceiling.
        var settings = Settings(burst: 10, perMinute: 55);

        var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new HardcoverRateLimiter(settings.HardcoverBurstLimit, settings.HardcoverPerMinuteLimit));

        Assert.IsTrue(ex.Message.Contains("60"), "exception should reference the documented per-minute ceiling");
    }

    [TestMethod]
    public void CreateOptions_ExactlyAtTheHard60PerMinuteCeilingSucceeds()
    {
        // Boundary: burst + perMinute == 60 is allowed, only > 60 is rejected.
        var options = HardcoverRateLimiter.CreateOptions(10, 50);

        Assert.AreEqual(10, options.TokenLimit);

        using var limiter = new HardcoverRateLimiter(10, 50);
        Assert.AreEqual(10, limiter.Capacity);
        Assert.AreEqual(50, limiter.TokensPerMinute);
    }

    [TestMethod]
    public async Task Handler_ARequestExactlyAtTheConfiguredDailyLimitSucceeds_TheNextThrows()
    {
        // Real counting behaviour: request N (== dailyLimit) succeeds, request N+1 is refused.
        const int dailyLimit = 4;
        var counts = new Dictionary<DateOnly, int>();
        _quotaRepository
            .Setup(r => r.TryConsumeAsync(It.IsAny<DateOnly>(), It.IsAny<int>()))
            .ReturnsAsync((DateOnly date, int limit) =>
            {
                counts.TryGetValue(date, out var current);
                if (current >= limit)
                {
                    return false;
                }

                counts[date] = current + 1;
                return true;
            });

        var inner = new StubHandler(HttpStatusCode.OK);
        using var client = MakeClient(Settings(daily: dailyLimit), inner);

        for (var i = 0; i < dailyLimit; i++)
        {
            var response = await client.GetAsync("https://api.hardcover.app/v1/graphql");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"request #{i + 1} (at/under the limit) should succeed");
        }

        Assert.AreEqual(dailyLimit, inner.CallCount);

        await Assert.ThrowsExactlyAsync<HardcoverDailyLimitExceededException>(
            () => client.GetAsync("https://api.hardcover.app/v1/graphql"));

        Assert.AreEqual(dailyLimit, inner.CallCount, "the request past the limit must never reach the inner handler");
    }

    [TestMethod]
    public async Task Handler_QuotaCounterIsScopedPerUtcDay_ANewDayStartsFresh()
    {
        // Simulates the persisted quota crossing a UTC day boundary: the repository's counts
        // are keyed per-DateOnly, so a day that already exhausted its budget must not affect
        // a different day's budget.
        var counts = new Dictionary<DateOnly, int>();
        _quotaRepository
            .Setup(r => r.TryConsumeAsync(It.IsAny<DateOnly>(), It.IsAny<int>()))
            .ReturnsAsync((DateOnly date, int limit) =>
            {
                counts.TryGetValue(date, out var current);
                if (current >= limit)
                {
                    return false;
                }

                counts[date] = current + 1;
                return true;
            });

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Exhaust "yesterday" directly against the repository, bypassing the handler (which
        // always stamps DateTime.UtcNow) - this stands in for a quota row left over from the
        // previous UTC day.
        Assert.IsTrue(await _quotaRepository.Object.TryConsumeAsync(yesterday, 1));
        Assert.IsFalse(await _quotaRepository.Object.TryConsumeAsync(yesterday, 1));

        var inner = new StubHandler(HttpStatusCode.OK);
        using var client = MakeClient(Settings(daily: 1), inner);

        // Today's counter is independent and starts fresh even though yesterday's is spent.
        var response = await client.GetAsync("https://api.hardcover.app/v1/graphql");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, inner.CallCount);
        Assert.AreEqual(1, counts[today]);
        Assert.AreEqual(1, counts[yesterday], "yesterday's counter must not have been touched by today's request");
    }

    [TestMethod]
    public async Task RetryHandler_ReacquiresARateLimitTokenOnEachRetryAttempt()
    {
        // HardcoverRetryHandler sits outside HardcoverRateLimitingHandler, so every retry
        // attempt - not just the first send - must travel back through the rate limiter (and
        // therefore re-consume the daily budget too). Chain: retry -> rate limit -> stub.
        var settings = Settings(daily: 5000, burst: 5, perMinute: 55);
        // The stub answers with a Retry-After the handler honors (see RetryAfterDelay), which
        // collapses the real 2s/4s/8s exponential backoff to ~nothing. Without it this single
        // test sleeps ~14s and dominates the whole backend suite; what is under test here is
        // that each retry re-enters the rate limiter, not how long Polly waits between them.
        var inner = new StubHandler(
            HttpStatusCode.ServiceUnavailable, // 5xx -> transient, retried
            retryAfter: TimeSpan.FromMilliseconds(1));

        var rateLimitingHandler = new HardcoverRateLimitingHandler(
            new HardcoverRateLimiter(settings.HardcoverBurstLimit, settings.HardcoverPerMinuteLimit),
            _scopeFactory,
            Options.Create(settings),
            new Mock<ILogger<HardcoverRateLimitingHandler>>().Object)
        {
            InnerHandler = inner,
        };

        var retryHandler = new HardcoverRetryHandler(new Mock<ILogger<HardcoverRetryHandler>>().Object)
        {
            InnerHandler = rateLimitingHandler,
        };

        using var client = new HttpClient(retryHandler);

        await client.GetAsync("https://api.hardcover.app/v1/graphql");

        // MaxRetryAttempts = 3, so the initial attempt plus 3 retries = 4 sends, each of
        // which must have gone through the rate limiter (and the daily-quota repository).
        Assert.AreEqual(4, inner.CallCount, "every retry attempt must reach the inner handler again");
        _quotaRepository.Verify(
            r => r.TryConsumeAsync(It.IsAny<DateOnly>(), It.IsAny<int>()),
            Times.Exactly(4));
    }

    private class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly TimeSpan? _retryAfter;

        public StubHandler(HttpStatusCode status, TimeSpan? retryAfter = null)
        {
            _status = status;
            _retryAfter = retryAfter;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(_status);
            if (_retryAfter is { } retryAfter)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            }

            return Task.FromResult(response);
        }
    }
}
