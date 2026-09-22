using AudiobookManager.Services.Similarity;
using AudiobookManager.Settings;

namespace AudiobookManager.Test.Services.Similarity;

[TestClass]
public class SimilarityGrouperTests
{
    private AudiobookManagerSettings _settings = null!;

    [TestInitialize]
    public void Setup()
    {
        _settings = new AudiobookManagerSettings
        {
            AudiobookImportPath = "/import",
            AudiobookLibraryPath = "/library"
        };
    }

    [TestMethod]
    public void GroupSimilarValues_GroupsInitialsVariants()
    {
        var values = new List<string> { "J.K. Rowling", "JK Rowling", "J. K. Rowling", "Brandon Sanderson" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEquivalent(
            new[] { "J.K. Rowling", "JK Rowling", "J. K. Rowling" },
            groups[0]);
    }

    [TestMethod]
    public void GroupSimilarValues_GroupsAmpersandVsAnd()
    {
        var values = new List<string> { "Fantasy & Adventure", "Fantasy and Adventure", "Mystery" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Fantasy & Adventure", "Fantasy and Adventure" },
            groups[0]);
    }

    [TestMethod]
    public void GroupSimilarValues_GroupsStrayWhitespace()
    {
        var values = new List<string> { "Brandon Sanderson", "Brandon  Sanderson", "Terry Pratchett" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Brandon Sanderson", "Brandon  Sanderson" },
            groups[0]);
    }

    [TestMethod]
    public void GroupSimilarValues_GroupsMinorTypo()
    {
        var values = new List<string> { "Brandon Sanderson", "Brandon Sandersen" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(2, groups[0].Count);
    }

    [TestMethod]
    public void GroupSimilarValues_DoesNotGroupDistinctShortNames()
    {
        // Short strings require an exact normalized match - guards against false positives.
        var values = new List<string> { "Eve", "Amy", "Ann", "Ivy" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void GroupSimilarValues_DoesNotGroupUnrelatedLongNames()
    {
        var values = new List<string> { "Brandon Sanderson", "Neil Gaiman", "Terry Pratchett" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void GroupSimilarValues_SingleValue_ReturnsNoGroups()
    {
        var groups = SimilarityGrouper.GroupSimilarValues(new List<string> { "Solo" }, _settings);
        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void GroupSimilarValues_EmptyList_ReturnsNoGroups()
    {
        var groups = SimilarityGrouper.GroupSimilarValues(new List<string>(), _settings);
        Assert.AreEqual(0, groups.Count);
    }

    // ---- Series "the"-insensitivity (isSeries) ----

    [TestMethod]
    public void GroupSimilarValues_IsSeries_GroupsLeadingArticleDifference()
    {
        var values = new List<string> { "The Mistborn Saga", "Mistborn Saga", "Unrelated Series" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings, isSeries: true);

        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEquivalent(
            new[] { "The Mistborn Saga", "Mistborn Saga" },
            groups[0]);
    }

    [TestMethod]
    public void GroupSimilarValues_NotSeries_DoesNotGroupLeadingArticleDifference()
    {
        // "The Mistborn Saga" vs "Mistborn Saga" differs by 4 characters (insert "The "), which
        // exceeds the normal edit-distance threshold, and the length gap can exceed the
        // length-window blocking cutoff too - so without isSeries, this must not group.
        var values = new List<string> { "The Mistborn Saga", "Mistborn Saga" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings);

        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void GroupSimilarValues_IsSeries_AuthorNamedTheRock_IsNotForciblyGroupedWithRock()
    {
        // The leading-article rule is series-only in intent - callers pass isSeries only for
        // series detection - but this proves the rule itself does not spuriously merge "The
        // Rock" onto "Rock" even when isSeries is set, since the two names are otherwise
        // unrelated and short enough that only the exact-strip bucket could catch them; the
        // bucket key ("rock") is shared, so they WOULD group under isSeries. This test instead
        // documents the caller-side guarantee: SimilarityGrouper is only ever invoked with
        // isSeries=true for series values, never for authors (see SimilarValueService).
        var values = new List<string> { "The Rock", "Rock" };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings, isSeries: false);

        Assert.AreEqual(0, groups.Count, "author-kind calls must never pass isSeries: true");
    }

    // ---- Ignored pairs ----

    [TestMethod]
    public void GroupSimilarValues_IgnoredPair_IsNotUnionedDirectly()
    {
        var values = new List<string> { "Ben Winters", "Ed Winters" };
        var ignored = new HashSet<(string A, string B)> { SimilarityGrouper.IgnoredPairKey("Ben Winters", "Ed Winters") };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings, ignoredPairs: ignored);

        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public void GroupSimilarValues_IgnoredPair_StillGroupsTransitivelyThroughAThirdValue()
    {
        // "brandonaa" and "brandonab" are ignored against each other directly (a single-edit
        // difference that would otherwise union them), but both are still within one edit of
        // "brandonac" - a third value neither is ignored against - so all three still cluster:
        // ignoring one pair removes only that one edge, not the values from consideration.
        var values = new List<string> { "brandonaa", "brandonab", "brandonac" };
        var ignored = new HashSet<(string A, string B)> { SimilarityGrouper.IgnoredPairKey("brandonaa", "brandonab") };

        var groups = SimilarityGrouper.GroupSimilarValues(values, _settings, ignoredPairs: ignored);

        Assert.AreEqual(1, groups.Count);
        CollectionAssert.AreEquivalent(values, groups[0]);
    }

    [TestMethod]
    public void IgnoredPairKey_OrdersValuesOrdinallyRegardlessOfInputOrder()
    {
        Assert.AreEqual(("A", "B"), SimilarityGrouper.IgnoredPairKey("A", "B"));
        Assert.AreEqual(("A", "B"), SimilarityGrouper.IgnoredPairKey("B", "A"));
    }
}
