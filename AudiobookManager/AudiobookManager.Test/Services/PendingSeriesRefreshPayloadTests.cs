using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The hand-versioned JSON contract stored in <c>pending_series_refresh.payload_json</c>.
/// Mirror of the book-level PendingRefreshPayload tests: the payload must survive a write/read
/// round trip unchanged, refuse anything that is not one of its own shapes, and refuse versions
/// a newer build may have written. The version guard is what lets a future field addition be
/// converted deliberately instead of silently misread.
/// </summary>
[TestClass]
public class PendingSeriesRefreshPayloadTests
{
    private static PendingSeriesRefreshPayload.Payload MakePayload() =>
        new(
            PendingSeriesRefreshPayload.CurrentVersion,
            "Mistborn",
            "Hardcover",
            "https://hardcover.app/series/42",
            "Mistborn Saga",
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            new List<PendingSeriesRefreshPayload.RosterEntry>
            {
                new("1", "The Final Empire", 2006, null, false),
                new("4", "Book B", 2010, null, false),
            },
            new List<PendingSeriesRefreshPayload.Change>
            {
                new(SeriesRefreshChangeType.PartUpdate, 5, "Book A", "01", "02", "Book A", null, null, null),
                new(SeriesRefreshChangeType.MissingBook, null, null, null, null, null, "4", "Book B", 2010),
                new(SeriesRefreshChangeType.PartRemoval, 7, "Book C", "3", null, "Book C", null, null, null),
            });

    [TestMethod]
    public void SerializeAndTryParse_RoundTripsEveryField()
    {
        var payload = MakePayload();

        var parsed = PendingSeriesRefreshPayload.TryParse(PendingSeriesRefreshPayload.Serialize(payload));

        Assert.IsNotNull(parsed);
        Assert.AreEqual(payload.Version, parsed.Version);
        Assert.AreEqual(payload.SeriesName, parsed.SeriesName);
        Assert.AreEqual(payload.SourceName, parsed.SourceName);
        Assert.AreEqual(payload.SourceUrl, parsed.SourceUrl);
        Assert.AreEqual(payload.SourceSeriesName, parsed.SourceSeriesName);
        Assert.AreEqual(payload.FetchedAt, parsed.FetchedAt);
        Assert.AreEqual(payload.Roster.Count, parsed.Roster.Count);
        Assert.AreEqual("1", parsed.Roster[0].Position);
        Assert.AreEqual("The Final Empire", parsed.Roster[0].Title);
        Assert.AreEqual(payload.Changes.Count, parsed.Changes.Count);

        var update = parsed.Changes.Single(c => c.Type == SeriesRefreshChangeType.PartUpdate);
        Assert.AreEqual(5, update.AudiobookId);
        Assert.AreEqual("02", update.NewPart);
        Assert.AreEqual("Book A", update.RosterTitle);

        var missing = parsed.Changes.Single(c => c.Type == SeriesRefreshChangeType.MissingBook);
        Assert.AreEqual("Book B", missing.Title);
        Assert.AreEqual("4", missing.Position);

        var removal = parsed.Changes.Single(c => c.Type == SeriesRefreshChangeType.PartRemoval);
        Assert.AreEqual(7, removal.AudiobookId);
        Assert.AreEqual("3", removal.StoredPart);
    }

    [TestMethod]
    public void TryParse_NullEmptyOrWhitespace_ReturnsNull()
    {
        Assert.IsNull(PendingSeriesRefreshPayload.TryParse(null));
        Assert.IsNull(PendingSeriesRefreshPayload.TryParse(""));
        Assert.IsNull(PendingSeriesRefreshPayload.TryParse("   "));
    }

    [TestMethod]
    public void TryParse_AForeignJsonBlob_ReturnsNull()
    {
        Assert.IsNull(PendingSeriesRefreshPayload.TryParse("{\"somethingElse\":true}"));
    }

    [TestMethod]
    public void TryParse_AVersionNewerThanThisBuildKnows_ReturnsNull()
    {
        // A version 999 payload is exactly what a future build would write after an additive
        // change. This build must refuse to render it half-converted rather than guess.
        var serialized = PendingSeriesRefreshPayload.Serialize(
            MakePayload() with { Version = 999 });

        Assert.IsNull(PendingSeriesRefreshPayload.TryParse(serialized));
    }

    [TestMethod]
    public void TryParse_AFutureVersionNumberInAnOtherwiseForeignBlob_ReturnsNull()
    {
        Assert.IsNull(PendingSeriesRefreshPayload.TryParse("{\"version\":999}"));
    }
}