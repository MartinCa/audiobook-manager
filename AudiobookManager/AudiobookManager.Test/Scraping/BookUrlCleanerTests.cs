using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class BookUrlCleanerTests
{
    // BookUrlCleaner applies one domain-agnostic rule today (strip query + fragment), so these
    // three sources behave identically. They're still asserted per-source, and separately from
    // Clean_LeavesAlreadyCleanUrlUnchanged below, so that if a source ever needs its own rule
    // (e.g. a canonical query parameter one of these sites starts requiring), a regression in
    // that source's stripping - or a change that stops leaving an already-clean URL from that
    // source alone - shows up as a named failure instead of being masked by the other sources.
    [TestMethod]
    [DataRow(
        "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=1672918775&sr=1-1&ref=a_search_c3_lProduct_1_1&pf_rd_p=83218cca-c308-412f-bfcf-90198b687a2f&pf_rd_r=G1S5EXVASGJFDXMCR9K5&pageLoadId=jKolYr0KYm4H5UUH&creativeId=0d6f6720-f41c-457e-a42b-8c8dceb62f2c",
        "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8",
        DisplayName = "Audible")]
    [DataRow(
        "https://www.goodreads.com/book/show/52578297-the-midnight-library?from_search=true&qid=abc123&rank=1",
        "https://www.goodreads.com/book/show/52578297-the-midnight-library",
        DisplayName = "Goodreads")]
    [DataRow(
        "https://hardcover.app/books/connections?utm_source=search&ref=abc123",
        "https://hardcover.app/books/connections",
        DisplayName = "Hardcover")]
    public void Clean_StripsTrackingParameters(string dirty, string expectedClean)
    {
        var result = BookUrlCleaner.Clean(dirty);

        Assert.AreEqual(expectedClean, result);
    }

    [TestMethod]
    [DataRow("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8", DisplayName = "Audible")]
    [DataRow("https://www.goodreads.com/book/show/52578297-the-midnight-library", DisplayName = "Goodreads")]
    [DataRow("https://hardcover.app/books/connections", DisplayName = "Hardcover")]
    public void Clean_LeavesAlreadyCleanUrlUnchanged(string clean)
    {
        var result = BookUrlCleaner.Clean(clean);

        Assert.AreEqual(clean, result);
    }

    [TestMethod]
    public void Clean_StripsFragment()
    {
        var dirty = "https://www.audible.com/pd/B07NZY2WT8?qid=123#reviews";

        var result = BookUrlCleaner.Clean(dirty);

        Assert.AreEqual("https://www.audible.com/pd/B07NZY2WT8", result);
    }

    [TestMethod]
    public void Clean_ReturnsOriginalValueForNullOrWhitespace()
    {
        Assert.IsNull(BookUrlCleaner.Clean(null!));
        Assert.AreEqual("", BookUrlCleaner.Clean(""));
        Assert.AreEqual("   ", BookUrlCleaner.Clean("   "));
    }

    [TestMethod]
    public void Clean_ReturnsOriginalValueForNonAbsoluteUrl()
    {
        var relative = "not-a-url";

        var result = BookUrlCleaner.Clean(relative);

        Assert.AreEqual(relative, result);
    }
}
