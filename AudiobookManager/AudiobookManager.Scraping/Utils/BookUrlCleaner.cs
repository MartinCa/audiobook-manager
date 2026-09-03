namespace AudiobookManager.Scraping.Utils;

/// <summary>
/// Strips tracking/session query parameters and fragments from book URLs (e.g. Audible's
/// ref=/pf_rd_*/pageLoadId params) while keeping the canonical scheme/host/path, which is all
/// Audible, Goodreads and Hardcover book pages need to identify the resource.
/// </summary>
public static class BookUrlCleaner
{
    public static string Clean(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.GetLeftPart(UriPartial.Path);
    }
}
