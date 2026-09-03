using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class ScraperUrlTests
{
    [TestMethod]
    [DataRow("https://audible.com/pd/book")]
    [DataRow("https://www.audible.com/pd/book")]
    [DataRow("http://www.audible.com/pd/book")]
    [DataRow("https://WWW.AUDIBLE.COM/pd/book")]
    [DataRow("https://deep.sub.audible.com/pd/book")]
    public void HasHost_UrlOnTheDomain_IsAccepted(string url)
    {
        Assert.IsTrue(ScraperUrl.HasHost(url, "audible.com"));
    }

    [TestMethod]
    // The substring check this replaced accepted every one of these.
    [DataRow("http://169.254.169.254/latest/meta-data?ref=audible.com")]
    [DataRow("http://192.168.1.1/admin#audible.com")]
    [DataRow("https://www.audible.com.example.net/pd/book")]
    [DataRow("https://notaudible.com/pd/book")]
    [DataRow("https://example.net/redirect?to=https://audible.com/pd/book")]
    [DataRow("https://example.net/audible.com/pd/book")]
    // Userinfo before the '@' is not the host: Uri.Host here is 169.254.169.254.
    [DataRow("https://audible.com@169.254.169.254/pd/book")]
    [DataRow("https://audible.com:pass@169.254.169.254/pd/book")]
    public void HasHost_UrlMerelyMentioningTheDomain_IsRejected(string url)
    {
        Assert.IsFalse(ScraperUrl.HasHost(url, "audible.com"));
    }

    [TestMethod]
    [DataRow("file:///etc/passwd")]
    [DataRow("ftp://audible.com/book")]
    public void HasHost_NonHttpScheme_IsRejected(string url)
    {
        // Even on the right host: these are not pages a scraper can read, and must never reach
        // HttpClient.
        Assert.IsFalse(ScraperUrl.HasHost(url, "audible.com"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a url")]
    [DataRow("/pd/book")]
    public void HasHost_NotAnAbsoluteUrl_IsRejected(string? url)
    {
        Assert.IsFalse(ScraperUrl.HasHost(url, "audible.com"));
    }

    [TestMethod]
    public void HasHost_FullyQualifiedTrailingDotHost_IsRejected()
    {
        // Uri does not strip the root label, so Host is "audible.com." - which is neither equal
        // to the domain nor a subdomain of it. Rejecting is the safe answer; pinned here so a
        // future "normalize the host first" change has to make that call deliberately.
        Assert.IsFalse(ScraperUrl.HasHost("https://audible.com./pd/book", "audible.com"));
    }

    [TestMethod]
    public void HasHost_OtherSourceDomains_BehaveTheSameWay()
    {
        Assert.IsTrue(ScraperUrl.HasHost("https://www.goodreads.com/book/show/1", "goodreads.com"));
        Assert.IsFalse(ScraperUrl.HasHost("https://evil.example/?x=goodreads.com", "goodreads.com"));

        Assert.IsTrue(ScraperUrl.HasHost("https://hardcover.app/books/x", "hardcover.app"));
        Assert.IsFalse(ScraperUrl.HasHost("https://evil.example/?x=hardcover.app", "hardcover.app"));
    }
}
