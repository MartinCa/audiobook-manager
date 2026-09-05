using System.Net;
using System.Net.Http;
using AudiobookManager.Scraping;

namespace AudiobookManager.Test.Scraping;

/// <summary>
/// The connection-level behaviour of <see cref="ProxyImageConnectGuard"/> wired into a real
/// <see cref="SocketsHttpHandler"/>: a literal private IP and a hostname that resolves to a
/// private address are both refused before any connection is attempted, and the refusal surfaces
/// as a request failure whose chain carries <see cref="NonPublicAddressException"/>.
/// </summary>
[TestClass]
public class ProxyImageConnectGuardConnectionTests
{
    private static HttpClient CreateClient(Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        var guard = new ProxyImageConnectGuard(resolve);
        var handler = new SocketsHttpHandler { ConnectCallback = guard.ConnectAsync };
        return new HttpClient(handler);
    }

    private static async Task AssertRefused(HttpClient client, string url)
    {
        try
        {
            await client.GetAsync(url);
            Assert.Fail($"Expected the request to {url} to be refused.");
        }
        catch (HttpRequestException ex)
        {
            Assert.IsTrue(
                HasNonPublicAddress(ex),
                $"Expected the exception chain to carry NonPublicAddressException; got {ex.Message.Split('\n')[0]}.");
        }
    }

    private static bool HasNonPublicAddress(HttpRequestException ex)
    {
        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner is NonPublicAddressException)
            {
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public async Task LiteralPrivateIp_IsRefused()
    {
        // A literal IP resolves to itself: 10.10.10.10 is RFC 1918, refused before connecting.
        var client = CreateClient((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }));
        await AssertRefused(client, "http://10.10.10.10/cover.jpg");
        // The cloud metadata address specifically.
        await AssertRefused(client, "https://169.254.169.254/latest/meta-data");
    }

    [TestMethod]
    public async Task HostnameResolvingToPrivateAddress_IsRefused()
    {
        // The hostname is innocent-looking, but its DNS answer is not. Resolving in-callback and
        // checking the answer is the whole point: the string in the URL is never trusted.
        var resolve = (string host, CancellationToken _) =>
            host == "metadata.internal" ? Task.FromResult(new[] { IPAddress.Parse("169.254.169.254") })
            : throw new Exception($"Unexpected host {host}");

        var client = CreateClient(resolve);
        await AssertRefused(client, "http://metadata.internal/latest/meta-data");
    }

    [TestMethod]
    public async Task MixedResolutionWithAnyPrivateAddress_IsRefused()
    {
        // Even one private address in the answer set is a refusal, so an attacker who can influence
        // DNS ordering cannot sneak a private address past under the cover of a public one.
        var resolve = (string host, CancellationToken _) =>
            Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.1") });

        var client = CreateClient(resolve);
        await AssertRefused(client, "http://clever-host.test/cover.jpg");
    }
}