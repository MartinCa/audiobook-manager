using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class BookUrlCleanerTests
{
    [TestMethod]
    public void Clean_StripsAudibleTrackingParameters()
    {
        var dirty = "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=1672918775&sr=1-1&ref=a_search_c3_lProduct_1_1&pf_rd_p=83218cca-c308-412f-bfcf-90198b687a2f&pf_rd_r=G1S5EXVASGJFDXMCR9K5&pageLoadId=jKolYr0KYm4H5UUH&creativeId=0d6f6720-f41c-457e-a42b-8c8dceb62f2c";

        var result = BookUrlCleaner.Clean(dirty);

        Assert.AreEqual("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8", result);
    }

    [TestMethod]
    public void Clean_StripsGoodreadsTrackingParameters()
    {
        var dirty = "https://www.goodreads.com/book/show/52578297-the-midnight-library?from_search=true&qid=abc123&rank=1";

        var result = BookUrlCleaner.Clean(dirty);

        Assert.AreEqual("https://www.goodreads.com/book/show/52578297-the-midnight-library", result);
    }

    [TestMethod]
    public void Clean_StripsFragment()
    {
        var dirty = "https://www.audible.com/pd/B07NZY2WT8?qid=123#reviews";

        var result = BookUrlCleaner.Clean(dirty);

        Assert.AreEqual("https://www.audible.com/pd/B07NZY2WT8", result);
    }

    [TestMethod]
    public void Clean_LeavesAlreadyCleanUrlUnchanged()
    {
        var clean = "https://hardcover.app/books/connections";

        var result = BookUrlCleaner.Clean(clean);

        Assert.AreEqual(clean, result);
    }

    [TestMethod]
    public void Clean_LeavesUrlWithoutQueryOrFragmentUnchanged()
    {
        var clean = "https://www.goodreads.com/book/show/52578297-the-midnight-library";

        var result = BookUrlCleaner.Clean(clean);

        Assert.AreEqual(clean, result);
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
