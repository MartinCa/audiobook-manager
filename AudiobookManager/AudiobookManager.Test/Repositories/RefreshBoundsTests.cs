using AudiobookManager.Database.Repositories;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class RefreshBoundsTests
{
    // A fixed non-UTC zone (UTC+5, no DST), so the Local-kind branch is exercised whatever zone
    // the test machine runs in - through TimeZoneInfo.Local, a UTC runner could not tell a
    // converted value from an unconverted one.
    private static readonly TimeZoneInfo PlusFive = TimeZoneInfo.CreateCustomTimeZone(
        "test-plus-5", TimeSpan.FromHours(5), "test-plus-5", "test-plus-5");

    [TestMethod]
    public void ToUtc_UtcKind_IsUnchangedEvenUnderANonUtcLocalZone()
    {
        var value = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        var result = RefreshBounds.ToUtc(value, PlusFive);
        Assert.AreEqual(value.Ticks, result.Ticks);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
    }

    [TestMethod]
    public void ToUtc_UnspecifiedKind_IsReadAsUtcWithoutShiftingEvenUnderANonUtcLocalZone()
    {
        var value = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Unspecified);
        var result = RefreshBounds.ToUtc(value, PlusFive);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(value.Ticks, result.Ticks);
    }

    [TestMethod]
    public void ToUtc_LocalKind_IsShiftedByTheLocalZonesOffset()
    {
        // 12:00 on a UTC+5 wall clock is 07:00 UTC. Dropping the Local branch would leave 12:00.
        var value = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Local);
        var result = RefreshBounds.ToUtc(value, PlusFive);
        Assert.AreEqual(new DateTime(2026, 6, 20, 7, 0, 0, DateTimeKind.Utc).Ticks, result.Ticks);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
    }

    [TestMethod]
    public void ToUtc_WithoutAZone_UsesTheMachinesLocalZone()
    {
        var instant = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(instant.Ticks, RefreshBounds.ToUtc(instant.ToLocalTime()).Ticks);
    }
}
