using System.Net;
using System.Net.Sockets;

namespace AudiobookManager.Scraping;

/// <summary>
/// Validates the destination address of every outbound connection made by the proxy-image
/// endpoint's <see cref="HttpClient"/>, refusing anything that is not a public internet address.
///
/// The endpoint deliberately fetches whatever http(s) URL a cover editor supplies, so it is open
/// forwarding by design. Its unacceptable part is the reach into the private network: a caller who
/// can reach this API can otherwise make the server issue requests to hosts only the server can
/// reach - 127.0.0.1, the rest of the LAN, and the cloud metadata service at 169.254.169.254,
/// which famously answers "who owns this server" with no authentication at all.
///
/// The check is done on the *resolved* address, at the moment of connection, inside
/// <see cref="SocketsHttpHandler.ConnectCallback"/>. That is what makes it actually hold up:
/// - A hostname is resolved here, and the addresses it yields are checked - not the literal
///   string in the URL. A name that resolves to 192.168.* behaves exactly like a literal
///   192.168.* in the URL, and has no way to sneak past.
/// - The address checked is the address connected to, so there is no check-then-connect race: the
///   DNS answer checked and the endpoint the socket binds to come from the same resolution.
/// - The handler follows redirects by default and the callback is invoked per new connection, so
///   a public host answering "302 Location: http://169.254.169.254/..." is caught at the hop.
/// - IPv4-mapped IPv6 addresses are normalized to IPv4 before the check, so an attacker who can
///   phrase the target as a mapped address gets the same answer.
///
/// It is deliberately not a host allowlist: covers legitimately live on every CDN and image host
/// the user cares to paste, and an allowlist would have to be maintained forever.
/// </summary>
public sealed class ProxyImageConnectGuard
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, bool> _isAllowed;

    /// <summary>
    /// <paramref name="resolve"/> is exposed so tests can stub DNS; production uses
    /// <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>, whose answers are the
    /// exact addresses the connection will be attempted against.
    ///
    /// <paramref name="isAllowed"/> defaults to <see cref="IsPublicAddress"/> and exists for the
    /// same reason: the redirect-at-the-hop behavior is only exercisable against a real local
    /// listener, which the production policy refuses, so tests override it to allow loopback while
    /// delegating everything else to the real policy.
    /// </summary>
    public ProxyImageConnectGuard(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, bool>? isAllowed = null)
    {
        _resolve = resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
        _isAllowed = isAllowed ?? IsPublicAddress;
    }

    /// <summary>
    /// Whether <paramref name="address"/> is one this application is willing to fetch from. The
    /// negation of "public internet address": loopback, private RFC 1918, link-local, the cloud
    /// metadata range (169.254/16 covers it), ULA, IPv4-mapped forms of all of the above, plus
    /// multicast and broadcast, which no HTTP server should be on.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var first = bytes[0];

            if (first == 0)
            {
                // 0.0.0.0/8 - "this network" / unspecified, nothing to fetch from.
                return false;
            }

            if (first == 10)
            {
                return false; // 10/8, RFC 1918.
            }

            if (first == 127)
            {
                return false; // 127/8, loopback.
            }

            if (first == 169 && bytes[1] == 254)
            {
                return false; // 169.254/16, link-local (and the cloud metadata service).
            }

            if (first == 172 && bytes[1] is >= 16 and <= 31)
            {
                return false; // 172.16/12, RFC 1918.
            }

            if (first == 192 && bytes[1] == 168)
            {
                return false; // 192.168/16, RFC 1918.
            }

            if (first >= 224)
            {
                return false; // 224/4 multicast, 240/4 reserved incl. 255.255.255.255 broadcast.
            }

            return true;
        }

        // IPv6.
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast)
        {
            return false;
        }

        var value = address.GetAddressBytes();

        // ::1 loopback (and ::, which nothing should fetch from either).
        if (value.All(b => b == 0) || (value[15] == 1 && value.Take(15).All(b => b == 0)))
        {
            return false;
        }

        // Unique local addresses fc00::/7.
        if ((value[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The <see cref="SocketsHttpHandler.ConnectCallback"/> body: resolve the hostname, refuse to
    /// open a connection to any non-public address it resolves to, then open the TCP connection
    /// to the first usable address (TLS is applied by the handler on top of the returned stream,
    /// so https keeps working with no extra work here).
    /// </summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        var addresses = await _resolve(host, cancellationToken);
        foreach (var address in addresses)
        {
            if (!_isAllowed(address))
            {
                throw new NonPublicAddressException(
                    $"Refusing to connect to non-public address {address} for host '{host}'.");
            }
        }

        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                socket.Dispose();
            }
        }

        throw lastFailure ?? new SocketException((int)SocketError.ConnectionRefused, $"No usable address for '{host}'.");
    }
}