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

    // Regression: without ignoring null fields on write, a row parsed from JSON that predates a
    // field (e.g. OriginalSeriesName, missing entirely from the stored bytes) would re-serialize
    // with that field spelled out as an explicit null - a byte-level difference from the
    // originally-stored payload with no underlying content change behind it. That is exactly what
    // MetadataRefreshService.ReevaluatePendingRefreshesAsync's ordinal payload comparison uses to
    // decide whether a row's snapshot actually changed, so a null field must never be written.
    [TestMethod]
    public void Serialize_NullFields_AreOmittedNotWrittenAsExplicitNull()
    {
        var serialized = PendingRefreshPayload.Serialize(new PendingRefreshPayload.Snapshot(
            PendingRefreshPayload.CurrentVersion,
            "https://x",
            "Audible",
            new List<string>(),
            new List<string>(),
            "Book",
            Subtitle: null,
            SeriesName: null,
            SeriesPart: null,
            Year: null,
            Genres: new List<string>(),
            Description: null,
            Language: null,
            Rating: null,
            Copyright: null,
            Publisher: null,
            Asin: null,
            OriginalSeriesName: null));

        Assert.IsFalse(serialized.Contains("null"), $"no field should serialize as an explicit null: {serialized}");
        Assert.IsFalse(serialized.Contains("originalSeriesName"));
        Assert.IsFalse(serialized.Contains("subtitle"));
    }

    // A legacy row written before OriginalSeriesName existed - no such key in the stored JSON at
    // all - must still parse (as null) and, once re-serialized, must not have that field
    // reappear: TryParse tolerates a missing key exactly the same way whether it predates the
    // field or the current write path simply omitted a null value.
    [TestMethod]
    public void TryParse_JsonPredatingOriginalSeriesName_ParsesWithNullAndRoundTripsWithoutTheKey()
    {
        const string legacyJson =
            "{\"version\":1,\"url\":\"https://x\",\"source\":\"Audible\"," +
            "\"authors\":[],\"narrators\":[],\"bookName\":\"Book\",\"genres\":[]}";

        var parsed = PendingRefreshPayload.TryParse(legacyJson);

        Assert.IsNotNull(parsed);
        Assert.IsNull(parsed.OriginalSeriesName);

        var reserialized = PendingRefreshPayload.Serialize(parsed);
        Assert.IsFalse(reserialized.Contains("originalSeriesName"));
    }
}