using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class PendingOnlineMatchPayloadTests
{
    [TestMethod]
    public void SerializeThenParse_RoundTripsResults()
    {
        var snapshot = new PendingRefreshPayload.Snapshot(
            PendingRefreshPayload.CurrentVersion,
            "https://www.audible.com/pd/test",
            "Audible",
            new List<string> { "Author One" },
            new List<string>(),
            "The Test Book",
            null,
            null,
            null,
            2010,
            new List<string> { "Fantasy" },
            null,
            null,
            null,
            null,
            null,
            null);

        var parsed = PendingOnlineMatchPayload.Parse(
            PendingOnlineMatchPayload.Serialize(new[] { snapshot }));

        Assert.AreEqual(1, parsed.Count);
        Assert.AreEqual(snapshot.BookName, parsed[0].BookName);
    }

    [TestMethod]
    public void Parse_BlankPayload_ReturnsEmptyList()
    {
        CollectionAssert.AreEqual(Array.Empty<PendingRefreshPayload.Snapshot>(), (System.Collections.ICollection)PendingOnlineMatchPayload.Parse(null));
        CollectionAssert.AreEqual(Array.Empty<PendingRefreshPayload.Snapshot>(), (System.Collections.ICollection)PendingOnlineMatchPayload.Parse("   "));
    }

    [TestMethod]
    public void Parse_UnreadableJson_ReturnsEmptyList()
    {
        var parsed = PendingOnlineMatchPayload.Parse("not json");

        Assert.AreEqual(0, parsed.Count);
    }

    [TestMethod]
    public void Parse_FutureVersion_ReturnsEmptyList()
    {
        var parsed = PendingOnlineMatchPayload.Parse(
            $"{{\"version\":{PendingOnlineMatchPayload.CurrentVersion + 1},\"results\":[]}}");

        Assert.AreEqual(0, parsed.Count);
    }

    // Regression guard: a corrupt payload with a valid version but a null "results" field used to
    // deserialize envelope.Results to null and return it as-is, so a caller reading .Count (e.g.
    // PendingOnlineMatchService.SelectResultAsync) would NRE instead of seeing zero candidates -
    // the same "never null-check before rendering" contract this class documents for every other
    // corrupt-payload case. Not reachable today (no writer in this app produces this shape), but
    // makes the null-safety contract airtight against a future writer bug or hand-edited row.
    [TestMethod]
    public void Parse_ValidVersionWithNullResults_ReturnsEmptyListRatherThanNull()
    {
        var parsed = PendingOnlineMatchPayload.Parse(
            $"{{\"version\":{PendingOnlineMatchPayload.CurrentVersion},\"results\":null}}");

        Assert.IsNotNull(parsed);
        Assert.AreEqual(0, parsed.Count);
    }
}
