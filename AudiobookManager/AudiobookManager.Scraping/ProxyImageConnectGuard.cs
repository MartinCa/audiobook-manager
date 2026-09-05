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
    /// Networks it is never safe to fetch from. Kept as <see cref="IPNetwork"/> (rather than raw
    /// octet arithmetic) so a range reads as itself; every entry below matches what the
    /// hand-rolled checks used to express. IPv4-mapped IPv6 addresses are normalized to IPv4 in
    /// <see cref="IsPublicAddress"/> before this list is consulted.
    /// </summary>
    private static readonly IPNetwork[] NonPublicNetworks =
    {
        // "This network" / unspecified - nothing to fetch from.
        IPNetwork.Parse("0.0.0.0/8"),
        // RFC 1918 private use.
        IPNetwork.Parse("10.0.0.0/8"),
        // Shared/CGNAT (RFC 6598): cloud and overlay networks (Tailscale, some Kubernetes/Docker
        // NAT) route internal-only traffic through this block.
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        // Loopback.
        IPNetwork.Parse("127.0.0.0/8"),
        // Link-local, which is also the cloud metadata service.
        IPNetwork.Parse("169.254.0.0/16"),
        // Multicast, then reserved (incl. 255.255.255.255 broadcast) - no HTTP server should be on either.
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
    };

    private static readonly IPNetwork[] NonPublicNetworksV6 =
    {
        // :: and ::1 - nothing to fetch from, loopback.
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        // Link-local and multicast.
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("ff00::/8"),
        // Unique local addresses fc00::/7.
        IPNetwork.Parse("fc00::/7"),
    };

    /// <summary>
    /// Whether <paramref name="address"/> is one this application is willing to fetch from. The
    /// negation of "public internet address": loopback, private RFC 1918, shared/CGNAT (RFC
    /// 6598), link-local, the cloud metadata range (169.254/16 covers it), ULA, IPv4-mapped
    /// forms of all of the above, plus multicast and broadcast, which no HTTP server should be
    /// on.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var networks = address.AddressFamily == AddressFamily.InterNetwork
            ? NonPublicNetworks
            : NonPublicNetworksV6;

        foreach (var network in networks)
        {
            if (network.Contains(address))
            {
                return false;
            }
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