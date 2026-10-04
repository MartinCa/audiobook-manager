using AudiobookManager.Database.Repositories;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class RefreshBoundsTests
{
    [TestMethod]
    public void ToUtc_UtcKind_IsUnchanged()
    {
        var value = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(value, RefreshBounds.ToUtc(value));
        Assert.AreEqual(DateTimeKind.Utc, RefreshBounds.ToUtc(value).Kind);
    }

    [TestMethod]
    public void ToUtc_UnspecifiedKind_IsReadAsUtcWithoutShifting()
    {
        var value = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Unspecified);
        var result = RefreshBounds.ToUtc(value);
        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(value.Ticks, result.Ticks);
    }

    [TestMethod]
    public void ToUtc_LocalKind_IsConvertedToTheSameInstant()
    {
        var instant = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(instant, RefreshBounds.ToUtc(instant.ToLocalTime()));
    }
}
