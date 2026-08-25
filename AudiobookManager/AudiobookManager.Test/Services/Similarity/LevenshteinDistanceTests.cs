using AudiobookManager.Services.Similarity;

namespace AudiobookManager.Test.Services.Similarity;

[TestClass]
public class LevenshteinDistanceTests
{
    [TestMethod]
    public void Compute_IdenticalStrings_ReturnsZero()
    {
        Assert.AreEqual(0, LevenshteinDistance.Compute("hello", "hello"));
    }

    [TestMethod]
    public void Compute_OneCharacterTypo_ReturnsOne()
    {
        Assert.AreEqual(1, LevenshteinDistance.Compute("sanderson", "sandersen"));
    }

    [TestMethod]
    public void Compute_EmptyStrings_ReturnsOtherLength()
    {
        Assert.AreEqual(5, LevenshteinDistance.Compute("", "hello"));
        Assert.AreEqual(5, LevenshteinDistance.Compute("hello", ""));
        Assert.AreEqual(0, LevenshteinDistance.Compute("", ""));
    }

    [TestMethod]
    public void Compute_CompletelyDifferentStrings_ReturnsMaxLength()
    {
        Assert.AreEqual(3, LevenshteinDistance.Compute("abc", "xyz"));
    }
}
