using System.Net;
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

    private class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StubHandler(HttpStatusCode status)
        {
            _status = status;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}
