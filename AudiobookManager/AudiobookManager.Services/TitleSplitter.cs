namespace AudiobookManager.Services;

/// <summary>
/// Optionally recovers a "Title: Subtitle" pair from a single title string - the shared helper
/// behind the "split title on colon" toggle every online-metadata apply point exposes, off by
/// default. Hardcover and Goodreads sometimes leave their own dedicated subtitle field blank and
/// fold the subtitle into the title instead, but nothing in either source documents which titles
/// follow that convention - a title's own colon can just as easily be part of the title itself
/// (the time in "4:50 from Paddington"), so guessing wrong silently corrupts a perfectly good
/// title. Scrapers therefore never split (see <see cref="AudiobookManager.Scraping.Scrapers"/>);
/// this is the one place a caller who does recognize a genuine "Title: Subtitle" title can choose
/// to split it, applied only where and when it is explicitly requested.
/// </summary>
public static class TitleSplitter
{
    /// <summary>
    /// Splits <paramref name="bookName"/> at the first ": " (colon-space) when
    /// <paramref name="splitOnColon"/> is true and <paramref name="subtitle"/> is blank. A bare
    /// colon with no following space (e.g. "4:50") is never treated as a separator. Returns the
    /// inputs unchanged when <paramref name="splitOnColon"/> is false or no ": " separator exists
    /// in the title. When a subtitle is already present the title is never re-split; only a title
    /// ending in ": &lt;that subtitle&gt;" loses the duplicated tail.
    /// </summary>
    public static (string BookName, string? Subtitle) Apply(string bookName, string? subtitle, bool splitOnColon)
    {
        if (!splitOnColon)
        {
            return (bookName, subtitle);
        }

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            // The source already carries the subtitle and repeats it in the title: drop the
            // duplicated ": <subtitle>" tail from the title and keep the subtitle as is.
            var suffix = ": " + subtitle.Trim();
            var trimmedName = bookName.TrimEnd();
            if (trimmedName.Length > suffix.Length
                && trimmedName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return (trimmedName[..^suffix.Length].Trim(), subtitle);
            }

            return (bookName, subtitle);
        }

        var separatorIndex = bookName.IndexOf(": ", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return (bookName, subtitle);
        }

        return (bookName[..separatorIndex].Trim(), bookName[(separatorIndex + 1)..].Trim());
    }
}
