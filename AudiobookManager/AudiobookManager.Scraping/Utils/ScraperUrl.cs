namespace AudiobookManager.Scraping.Utils;

/// <summary>
/// Decides whether a URL actually belongs to a scraper's source.
///
/// Each scraper's <c>SupportsUrl</c> used to be <c>url.Contains("audible.com")</c> and its
/// equivalents. A substring test is not a host test: it is satisfied anywhere in the string, so
/// <c>http://169.254.169.254/latest/meta-data?ref=audible.com</c> and
/// <c>https://www.audible.com.example.net/pd/x</c> both passed it. Since the URL reaching
/// <c>GetBookDetails</c> and the series-by-URL lookup comes straight from the caller, and the
/// scraped page's parsed content is returned in the response, that turned "which source is this
/// link from?" into a request-forgery primitive against hosts only the server can reach.
///
/// Matching on the parsed <see cref="Uri.Host"/> - equal to the domain, or ending in a dot
/// followed by it - is the check that was intended. The dot matters: without it
/// "notaudible.com" would match "audible.com" the same way the substring test did.
/// </summary>
public static class ScraperUrl
{
    /// <summary>
    /// Whether <paramref name="url"/> is an absolute http(s) URL whose host is
    /// <paramref name="domain"/> or a subdomain of it.
    /// </summary>
    public static bool HasHost(string? url, string domain)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // A scraper only ever fetches over http(s); anything else (file:, ftp:, a UNC path) is
        // not a page this application knows how to read and must not be handed to HttpClient.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host;

        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);
    }
}
