using AudiobookManager.Services.Similarity;

namespace AudiobookManager.Test.Services.Similarity;

[TestClass]
public class NameNormalizerTests
{
    [TestMethod]
    public void Normalize_MergesSingleLetterInitials()
    {
        Assert.AreEqual("jk rowling", NameNormalizer.Normalize("J. K. Rowling"));
        Assert.AreEqual("jk rowling", NameNormalizer.Normalize("JK Rowling"));
    }

    [TestMethod]
    public void Normalize_ReplacesAmpersandWithAnd()
    {
        Assert.AreEqual(
            NameNormalizer.Normalize("Fantasy and Adventure"),
            NameNormalizer.Normalize("Fantasy & Adventure"));
    }

    [TestMethod]
    public void Normalize_CollapsesWhitespace()
    {
        Assert.AreEqual("john smith", NameNormalizer.Normalize("  John   Smith  "));
    }

    [TestMethod]
    public void Normalize_StripsPeriods()
    {
        Assert.AreEqual("mr smith", NameNormalizer.Normalize("Mr. Smith"));
    }

    [TestMethod]
    public void Normalize_LowercasesAndTrims()
    {
        Assert.AreEqual("brandon sanderson", NameNormalizer.Normalize("  Brandon Sanderson  "));
    }

    [TestMethod]
    public void Normalize_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, NameNormalizer.Normalize(null));
        Assert.AreEqual(string.Empty, NameNormalizer.Normalize("   "));
    }
}
