using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using AudiobookManager.Scraping;

namespace AudiobookManager.Test.Scraping;

/// <summary>
/// The redirect-at-the-hop behaviour the issue calls out as the one most likely to be missed: a
/// perfectly public host can answer "302 Location: http://169.254.169.254/..." and a client that
/// validates only the original URL lets the private destination through. Because the guard is a
/// <see cref="SocketsHttpHandler.ConnectCallback"/>, the handler invokes it again for every host
/// a redirect lands on, and the destination is validated then too.
///
/// These tests run against real loopback listeners, which the production policy refuses - the
/// guard instances here use the constructor's policy seam to allow <c>127.0.0.1</c> while
/// delegating everything else to the production <see cref="ProxyImageConnectGuard.IsPublicAddress"/>.
/// </summary>
[TestClass]
public class ProxyImageConnectGuardRedirectTests
{
    // Tracked so the test cleanup below can stop them all: a TcpListener keeps its port bound
    // until Stop() is called, and leaked listeners would tie up loopback ports across tests.
    private readonly List<TcpListener> _listeners = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
            }
            catch
            {
                // Already stopped or never started; nothing to clean up.
            }
        }

        _listeners.Clear();
    }

    // The seam used by every test here: loopback is allowed so the local listeners are reachable,
    // and everything else is judged by the real production policy.
    private static bool TestPolicy(IPAddress a) =>
        a.Equals(IPAddress.Loopback) || ProxyImageConnectGuard.IsPublicAddress(a);

    private static ProxyImageConnectGuard TestGuard(Func<string, CancellationToken, Task<IPAddress[]>> resolve) =>
        new(resolve, TestPolicy);

    private async Task<IPEndPoint> StartListener(Func<NetworkStream, Task> serve)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        _ = AcceptLoop(listener, serve);
        return endpoint;
    }

    private static async Task AcceptLoop(TcpListener listener, Func<NetworkStream, Task> serve)
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        using (var stream = client.GetStream())
                        {
                            await serve(stream);
                        }
                    }
                    catch
                    {
                        // The test's request failed or the client hung up; nothing to do here.
                    }
                });
            }
        }
        catch
        {
            // Listener is being shut down by the test.
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string response)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task DrainRequestAsync(NetworkStream stream)
    {
        // Read until the end of the request headers so the server knows the client is done
        // writing; the byte count is irrelevant, only the terminator matters.
        var buffer = new byte[1024];
        var builder = new StringBuilder();
        while (!builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (builder.Length > 8192)
            {
                break;
            }
        }
    }

    private static bool WrapsNonPublicAddress(Exception? ex)
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
    public async Task RedirectToPrivateAddress_IsRefusedAtTheHop()
    {
        // A public-looking host (here: loopback allowed only by the test seam) answers a 302 that
        // points at the cloud metadata service. The guard must refuse it on the redirect hop, not
        // fetch it.
        var endpoint = await StartListener(async stream =>
        {
            await DrainRequestAsync(stream);
            await WriteResponseAsync(stream,
                "HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/latest/meta-data\r\nContent-Length: 0\r\n\r\n");
        });

        // Both hops use the real policy via TestPolicy: 127.0.0.1 is allowed (loopback seam), the
        // metadata literal is refused.
        var resolve = (string host, CancellationToken _) => Task.FromResult(new[] { IPAddress.Parse(host) });
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = TestGuard(resolve).ConnectAsync });

        try
        {
            await client.GetAsync(new Uri($"http://127.0.0.1:{endpoint.Port}/cover.jpg"), HttpCompletionOption.ResponseHeadersRead);
            Assert.Fail("Expected the redirect to the metadata service to be refused.");
        }
        catch (HttpRequestException ex)
        {
            Assert.IsTrue(
                WrapsNonPublicAddress(ex),
                $"Expected the redirect hop to be refused; got {ex.Message.Split('\n')[0]}.");
        }
    }

    [TestMethod]
    public async Task RedirectToPublicHost_StillWorks()
    {
        // A redirect to another public host (a CDN pattern) still fetches fine: the callback fires
        // for the redirect target too, and a public target passes.
        var cdnEndpoint = await StartListener(async stream =>
        {
            await DrainRequestAsync(stream);
            await WriteResponseAsync(stream,
                "HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\nContent-Length: 4\r\n\r\nabcd");
        });

        var redirectEndpoint = await StartListener(async stream =>
        {
            await DrainRequestAsync(stream);
            await WriteResponseAsync(stream,
                $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{cdnEndpoint.Port}/real.jpg\r\nContent-Length: 0\r\n\r\n");
        });

        var resolve = (string host, CancellationToken _) => Task.FromResult(new[] { IPAddress.Parse(host) });
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = TestGuard(resolve).ConnectAsync });

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{redirectEndpoint.Port}/cover.jpg"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }
}