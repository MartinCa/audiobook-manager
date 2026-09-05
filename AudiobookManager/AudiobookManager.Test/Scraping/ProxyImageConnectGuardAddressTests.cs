using System.Net;
using AudiobookManager.Scraping;

namespace AudiobookManager.Test.Scraping;

/// <summary>
/// The address ranges the proxy-image guard refuses. This is the load-bearing table for the SSRF
/// work: the endpoint must fetch any public URL a user pastes, and must never reach into the
/// private network - loopback, RFC 1918, link-local (which includes the cloud metadata service),
/// ULA, multicast, broadcast, and the IPv4-mapped forms of all of the above.
/// </summary>
[TestClass]
public class ProxyImageConnectGuardAddressTests
{
    private static IPAddress IPv4(string s) => IPAddress.Parse(s);

    private static IPAddress V4Mapped(string s) =>
        IPAddress.Parse(s).MapToIPv6();

    [TestMethod]
    // The issue's own list: "127.0.0.0/8, 10/8, 172.16/12, 192.168/16, 169.254/16 (the cloud
    // metadata range), ::1, fc00::/7, fe80::/10, and IPv4-mapped forms of the same".
    [DataRow("127.0.0.1")]
    [DataRow("127.255.255.255")]
    [DataRow("10.0.0.1")]
    [DataRow("10.255.255.255")]
    [DataRow("172.16.0.1")]
    [DataRow("172.31.255.255")]
    [DataRow("192.168.0.1")]
    [DataRow("192.168.255.255")]
    [DataRow("169.254.0.1")]
    [DataRow("169.254.169.254")] // cloud metadata
    [DataRow("169.254.255.255")]
    [DataRow("100.64.0.1")] // shared/CGNAT (RFC 6598)
    [DataRow("100.127.255.255")] // top of 100.64/10
    [DataRow("0.0.0.0")]
    [DataRow("0.255.255.255")]
    [DataRow("224.0.0.1")] // multicast, not unicast
    [DataRow("239.255.255.255")]
    [DataRow("240.0.0.1")] // reserved
    [DataRow("255.255.255.255")] // broadcast
    [DataRow("::1")]
    [DataRow("::")]
    [DataRow("fe80::1")] // IPv6 link-local
    [DataRow("fc00::1")]
    [DataRow("fd12:3456:789a:1::1")] // ULA
    [DataRow("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [DataRow("::ffff:10.0.0.1")] // IPv4-mapped RFC 1918
    [DataRow("::ffff:192.168.1.1")] // IPv4-mapped RFC 1918
    [DataRow("::ffff:169.254.169.254")] // IPv4-mapped cloud metadata
    [DataRow("::ffff:100.64.0.1")] // IPv4-mapped shared/CGNAT
    public void IsPublicAddress_NonPublicRange_IsRefused(string address)
    {
        Assert.IsFalse(ProxyImageConnectGuard.IsPublicAddress(IPAddress.Parse(address)));
    }

    [TestMethod]
    [DataRow("8.8.8.8")]
    [DataRow("1.1.1.1")]
    [DataRow("93.184.216.34")] // example.com
    [DataRow("172.32.0.1")] // just outside 172.16/12
    [DataRow("169.253.255.255")] // just outside 169.254/16
    [DataRow("192.169.0.1")] // just outside 192.168/16
    [DataRow("100.63.255.255")] // just below 100.64/10
    [DataRow("100.128.0.1")] // just above 100.64/10
    [DataRow("223.255.255.255")] // top of unicast
    [DataRow("2001:4860:4860::8888")] // Google DNS v6
    [DataRow("2606:4700:10::6814:179a")] // Cloudflare/example
    public void IsPublicAddress_PublicAddress_IsAllowed(string address)
    {
        Assert.IsTrue(ProxyImageConnectGuard.IsPublicAddress(IPAddress.Parse(address)));
    }

    [TestMethod]
    // IPv4-mapped forms of public IPv4 addresses are normalized and allowed, exactly as the
    // mapped private ones are normalized and refused.
    [DataRow("::ffff:8.8.8.8")]
    public void IsPublicAddress_IPv4MappedPublicAddress_IsAllowed(string address)
    {
        Assert.IsTrue(ProxyImageConnectGuard.IsPublicAddress(IPAddress.Parse(address)));
    }

    [TestMethod]
    public void IsPublicAddress_V4MappedAndPlain_Agree()
    {
        foreach (var s in new[] { "10.1.2.3", "172.19.0.1", "192.168.7.7", "127.0.0.1", "169.254.169.254" })
        {
            var plain = IPv4(s);
            var mapped = V4Mapped(s);
            Assert.AreEqual(
                ProxyImageConnectGuard.IsPublicAddress(plain),
                ProxyImageConnectGuard.IsPublicAddress(mapped),
                $"Mapped and plain forms of {s} must agree.");
        }
    }
}