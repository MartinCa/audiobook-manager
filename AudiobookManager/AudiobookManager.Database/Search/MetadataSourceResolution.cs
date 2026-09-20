using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Search;

/// <summary>
/// Resolves which metadata source a book's <c>Www</c> URL belongs to, for the book-list source
/// filter. The domain table below is a deliberate, small duplicate of the host each IScraper's
/// SupportsUrl checks (AudibleScraper/GoodreadsScraper/HardcoverScraper) - it cannot call those
/// directly, since AudiobookManager.Scraping references AudiobookManager.Database (not the other
/// way around) and this needs to run inside a Database-layer save interceptor and SQL backfill.
/// Matching mirrors ScraperUrl.HasHost: the host must equal the domain or be a subdomain of it.
/// </summary>
public static class MetadataSourceResolution
{
    public const string SqlFunctionName = "resolve_metadata_source";

    // Internal (rather than private) so MetadataSourceResolutionScraperParityTests can assert
    // this table actually agrees with every registered IScraper's real SupportsUrl domain,
    // rather than only ever comparing itself to itself.
    internal static readonly (string SourceName, string Domain)[] KnownSources =
    {
        ("Audible", "audible.com"),
        ("Goodreads", "goodreads.com"),
        ("Hardcover", "hardcover.app"),
    };

    /// <summary>
    /// Marker for use inside LINQ query expressions - EF Core translates calls to this specific
    /// method into a call to the <see cref="SqlFunctionName"/> SQL function (registered via
    /// <see cref="Register"/>) rather than invoking this body, which is never reached at runtime.
    /// </summary>
    [DbFunction(SqlFunctionName)]
    public static string? Resolve(string? url) =>
        throw new NotSupportedException($"{nameof(Resolve)} can only be used inside a LINQ query expression.");

    /// <summary>
    /// The actual CLR implementation, used both as the registered SQL function body (for the
    /// one-time migration backfill of existing rows) and by the save interceptor that keeps
    /// Audiobook.MatchedSourceName in sync with Www going forward.
    /// </summary>
    public static string? ResolvePlain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host;
        foreach (var (sourceName, domain) in KnownSources)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase))
            {
                return sourceName;
            }
        }

        return null;
    }

    public static void Register(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>(SqlFunctionName, ResolvePlain);
    }
}
