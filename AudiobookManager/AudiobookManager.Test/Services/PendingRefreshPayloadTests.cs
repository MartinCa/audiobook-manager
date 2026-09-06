using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class PendingRefreshPayloadTests
{
    [TestMethod]
    public void SerializeThenParse_RoundTripsEveryField()
    {
        var snapshot = new PendingRefreshPayload.Snapshot(
            PendingRefreshPayload.CurrentVersion,
            "https://www.audible.com/pd/test",
            "Audible",
            new List<string> { "Author One", "Author Two" },
            new List<string> { "Narrator" },
            "The Test Book",
            "A Subtitle",
            "The Series",
            "3",
            2010,
            new List<string> { "Fantasy" },
            "A description.",
            "en",
            "4.5",
            "2020 Publisher",
            "The Publisher",
            "B0123456789");

        var parsed = PendingRefreshPayload.TryParse(PendingRefreshPayload.Serialize(snapshot));

        Assert.IsNotNull(parsed);
        Assert.AreEqual(snapshot.Version, parsed.Version);
        Assert.AreEqual(snapshot.Url, parsed.Url);
        Assert.AreEqual(snapshot.Source, parsed.Source);
        CollectionAssert.AreEqual(snapshot.Authors.ToList(), parsed.Authors.ToList());
        CollectionAssert.AreEqual(snapshot.Narrators.ToList(), parsed.Narrators.ToList());
        Assert.AreEqual(snapshot.BookName, parsed.BookName);
        Assert.AreEqual(snapshot.Subtitle, parsed.Subtitle);
        Assert.AreEqual(snapshot.SeriesName, parsed.SeriesName);
        Assert.AreEqual(snapshot.SeriesPart, parsed.SeriesPart);
        Assert.AreEqual(snapshot.Year, parsed.Year);
        CollectionAssert.AreEqual(snapshot.Genres.ToList(), parsed.Genres.ToList());
        Assert.AreEqual(snapshot.Description, parsed.Description);
        Assert.AreEqual(snapshot.Language, parsed.Language);
        Assert.AreEqual(snapshot.Rating, parsed.Rating);
        Assert.AreEqual(snapshot.Copyright, parsed.Copyright);
        Assert.AreEqual(snapshot.Publisher, parsed.Publisher);
        Assert.AreEqual(snapshot.Asin, parsed.Asin);
    }

    [TestMethod]
    public void TryParse_NewerVersion_ReturnsNull()
    {
        // A payload written by a future build must not be rendered half-converted.
        var future = $$"""{"version":{{PendingRefreshPayload.CurrentVersion + 1}},"url":"https://x"}""";

        Assert.IsNull(PendingRefreshPayload.TryParse(future));
    }

    [TestMethod]
    public void TryParse_ForeignJson_ReturnsNull()
    {
        Assert.IsNull(PendingRefreshPayload.TryParse("{\"something\":\"else\"}"));
        Assert.IsNull(PendingRefreshPayload.TryParse("not json at all"));
        Assert.IsNull(PendingRefreshPayload.TryParse(""));
        Assert.IsNull(PendingRefreshPayload.TryParse(null));
    }

    [TestMethod]
    public void Serialize_UsesWebDefaults_SnakeCaseLowercase()
    {
        // The contract is hand-versioned, so its on-disk shape is part of it: camelCase keys
        // (JsonSerializerDefaults.Web) rather than the CLR default (PascalCase).
        var serialized = PendingRefreshPayload.Serialize(new PendingRefreshPayload.Snapshot(
            1, "https://x", "Audible",
            new List<string>(), new List<string>(), "Book", null, null, null, null,
            new List<string>(), null, null, null, null, null, null));

        StringAssert.Contains(serialized, "\"bookName\"");
        Assert.IsFalse(serialized.Contains("\"BookName\""), "the payload must not use CLR PascalCase keys");
    }
}