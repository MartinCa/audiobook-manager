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
}
