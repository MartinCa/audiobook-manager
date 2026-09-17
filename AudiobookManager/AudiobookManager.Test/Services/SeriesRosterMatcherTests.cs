using AudiobookManager.Database.Sort;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class SeriesRosterMatcherTests
{
    /// <summary>
    /// The presentation order every roster-ordering caller relies on (the series detail's
    /// missing / ignored / part-mismatch sections and the refresh diff's three change lists):
    /// numeric positions by value, named positions after them alphabetically, blanks last.
    ///
    /// Regression guard for the tier collapse: blanks used to key on <c>Double.MaxValue</c> and
    /// named positions on <c>Double.MaxValue - 1</c>, which is the *same* double, so the two
    /// tiers tied and the empty text a blank pairs with sorted every blank ahead of every named
    /// position. Reverting PositionSortKey to those two literals puts &lt;null&gt; and "" first
    /// here and fails this test.
    /// </summary>
    [TestMethod]
    public void PositionSortKey_BlankPositions_SortAfterNamedOnes()
    {
        var positions = new string?[] { "2", null, "Prequel", "1", "Book 3", "   " };

        var sorted = positions
            .OrderBy(SeriesRosterMatcher.PositionSortKey)
            .ToList();

        CollectionAssert.AreEqual(
            new string?[] { "1", "2", "Book 3", "Prequel", null, "   " },
            sorted);
    }

    /// <summary>
    /// The blank tier is only last because it is <c>Double.PositiveInfinity</c> rather than
    /// <c>Double.MaxValue</c>; asserting the two tiers are distinct doubles states the property
    /// the ordering above rests on directly, rather than only through a sort.
    /// </summary>
    [TestMethod]
    public void PositionSortKey_BlankAndNamedTiers_AreDistinctKeys()
    {
        var blank = SeriesRosterMatcher.PositionSortKey(null).Numeric;
        var named = SeriesRosterMatcher.PositionSortKey("Prequel").Numeric;

        Assert.AreNotEqual(named, blank);
        Assert.IsTrue(named < blank, "a named position must sort before a blank one");
        Assert.IsTrue(SeriesRosterMatcher.PositionSortKey("17.5").Numeric < named,
            "every finite numeric position must sort before a named one");
    }

    /// <summary>
    /// The tier decision is <see cref="SeriesPartSortKey.KeyPlain"/>'s, so the special double
    /// tokens it demotes are demoted here too. Under the old restated key these parsed with
    /// <c>NumberStyles.Any</c> and no finiteness guard: "NaN" sorted ahead of everything
    /// (<c>Comparer&lt;double&gt;</c> orders NaN first) and "Infinity" landed on the blank tier.
    /// </summary>
    [TestMethod]
    public void PositionSortKey_SpecialDoubleTokens_AreNamedPositionsNotNumericOnes()
    {
        foreach (var token in new[] { "NaN", "Infinity", "-Infinity", "1e309" })
        {
            Assert.AreEqual(
                SeriesPartSortKey.NonNumericTier,
                SeriesRosterMatcher.PositionSortKey(token).Numeric,
                $"'{token}' must sort as a named position");
            Assert.AreEqual(token, SeriesRosterMatcher.PositionSortKey(token).Text);
        }
    }

    /// <summary>
    /// Only named positions carry a text tiebreaker - a numeric position is fully ordered by its
    /// value and a blank one has nothing to distinguish it.
    /// </summary>
    [TestMethod]
    public void PositionSortKey_NumericAndBlankPositions_CarryNoTextTiebreaker()
    {
        Assert.AreEqual(string.Empty, SeriesRosterMatcher.PositionSortKey("2.5").Text);
        Assert.AreEqual(string.Empty, SeriesRosterMatcher.PositionSortKey(null).Text);
        Assert.AreEqual(string.Empty, SeriesRosterMatcher.PositionSortKey("  ").Text);
        Assert.AreEqual("Book 3", SeriesRosterMatcher.PositionSortKey("  Book 3  ").Text);
    }
}
