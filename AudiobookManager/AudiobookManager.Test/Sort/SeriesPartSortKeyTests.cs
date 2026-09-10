using AudiobookManager.Database.Sort;

namespace AudiobookManager.Test.Sort;

[TestClass]
public class SeriesPartSortKeyTests
{
    /// <summary>
    /// The non-numeric tier. <c>Double.MaxValue - 1</c> rounds back to <c>Double.MaxValue</c>
    /// in the CLR's own double arithmetic (the ULP at that magnitude is ~2^971), so this is
    /// the exact value <c>KeyPlain</c> returns for any non-numeric non-blank part - including
    /// the special strings that must never land on the blank tier or sort first as NULL.
    /// </summary>
    private const double NonNumericTier = double.MaxValue;

    [TestMethod]
    public void KeyPlain_SpecialDoubleTokens_AreRejectedToTheNonNumericTier()
    {
        // "NaN" / "Infinity" / "-Infinity" parse under any NumberStyles with InvariantCulture,
        // and "1e309" parses to +Infinity under Float's AllowExponent - only the IsFinite guard
        // keeps them off the NULL-first (NaN) and blank (Infinity) tiers.
        Assert.AreEqual(NonNumericTier, SeriesPartSortKey.KeyPlain("NaN"));
        Assert.AreEqual(NonNumericTier, SeriesPartSortKey.KeyPlain("Infinity"));
        Assert.AreEqual(NonNumericTier, SeriesPartSortKey.KeyPlain("-Infinity"));
        Assert.AreEqual(NonNumericTier, SeriesPartSortKey.KeyPlain("1e309"));
    }

    [TestMethod]
    public void KeyPlain_NegativeAndDecimalParts_KeepTheirNumericValue()
    {
        Assert.AreEqual(-2.5, SeriesPartSortKey.KeyPlain("-2.5"));
        Assert.AreEqual(17.5, SeriesPartSortKey.KeyPlain("17.5"));
        Assert.AreEqual(1, SeriesPartSortKey.KeyPlain("1"));
    }
}