using AudiobookManager.Domain;

namespace AudiobookManager.Test.DomainModels;

[TestClass]
public class ExpectedBookClassifierTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    [TestMethod]
    public void IsUpcoming_ReleaseDateInTheFuture_ReturnsTrue()
    {
        Assert.IsTrue(ExpectedBookClassifier.IsUpcoming(Today.AddDays(1), null, Today));
    }

    [TestMethod]
    public void IsUpcoming_ReleaseDateToday_ReturnsFalse()
    {
        // "Upcoming" means strictly after today - a book releasing today is already out.
        Assert.IsFalse(ExpectedBookClassifier.IsUpcoming(Today, null, Today));
    }

    [TestMethod]
    public void IsUpcoming_ReleaseDateInThePast_ReturnsFalse()
    {
        Assert.IsFalse(ExpectedBookClassifier.IsUpcoming(Today.AddDays(-1), null, Today));
    }

    [TestMethod]
    public void IsUpcoming_ReleaseDatePresent_TakesPrecedenceOverYear()
    {
        // A precise past release date wins even when Year alone would suggest "upcoming" (a
        // stale/incorrect Year should not override the more precise field).
        Assert.IsFalse(ExpectedBookClassifier.IsUpcoming(Today.AddDays(-1), Today.Year + 5, Today));
    }

    [TestMethod]
    public void IsUpcoming_NoReleaseDate_YearInTheFuture_ReturnsTrue()
    {
        Assert.IsTrue(ExpectedBookClassifier.IsUpcoming(null, Today.Year + 1, Today));
    }

    [TestMethod]
    public void IsUpcoming_NoReleaseDate_YearIsThisYear_ReturnsFalse()
    {
        Assert.IsFalse(ExpectedBookClassifier.IsUpcoming(null, Today.Year, Today));
    }

    [TestMethod]
    public void IsUpcoming_NoReleaseDate_YearInThePast_ReturnsFalse()
    {
        Assert.IsFalse(ExpectedBookClassifier.IsUpcoming(null, Today.Year - 1, Today));
    }

    [TestMethod]
    public void IsUpcoming_NeitherReleaseDateNorYear_ReturnsFalse()
    {
        // No date at all is never treated as "not yet released" - it is missing.
        Assert.IsFalse(ExpectedBookClassifier.IsUpcoming(null, null, Today));
    }
}
