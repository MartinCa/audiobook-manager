using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class MetadataSearchResultTests
{
    [TestMethod]
    public void Url_IsNotMutatedByCleaning()
    {
        var dirty = "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=a_search";

        var result = new MetadataSearchResult(dirty, "Winter Dark");

        Assert.AreEqual(dirty, result.Url, "Url must stay a faithful record of what was fetched.");
    }

    [TestMethod]
    public void CleanUrl_StripsTrackingParameters()
    {
        var dirty = "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=a_search";

        var result = new MetadataSearchResult(dirty, "Winter Dark");

        Assert.AreEqual("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8", result.CleanUrl);
    }

    [TestMethod]
    public void CleanUrl_MatchesUrlWhenAlreadyClean()
    {
        var clean = "https://hardcover.app/books/connections";

        var result = new MetadataSearchResult(clean, "Connections");

        Assert.AreEqual(clean, result.CleanUrl);
    }

    [TestMethod]
    public void CleanUrl_ReflectsUrlMutatedAfterConstruction()
    {
        var result = new MetadataSearchResult("https://hardcover.app/books/connections", "Connections")
        {
            Url = "https://www.goodreads.com/book/show/1-connections?ref=x",
        };

        Assert.AreEqual("https://www.goodreads.com/book/show/1-connections", result.CleanUrl);
    }
}
