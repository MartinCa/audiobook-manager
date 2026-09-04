namespace AudiobookManager.Scraping;

/// <summary>
/// Thrown by <see cref="ProxyImageConnectGuard"/> when a connection is refused because the
/// destination resolves to a private, loopback, link-local or otherwise non-public address. The
/// controller catches this specifically so a refused URL reads as a caller error (the URL they
/// supplied is not fetchable) rather than an unhandled server-side failure.
/// </summary>
public sealed class NonPublicAddressException : InvalidOperationException
{
    public NonPublicAddressException(string message)
        : base(message)
    {
    }
}