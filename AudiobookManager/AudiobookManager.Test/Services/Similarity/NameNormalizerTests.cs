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

    [TestMethod]
    public void StripLeadingArticle_RemovesLeadingThe()
    {
        Assert.AreEqual(
            "mistborn saga",
            NameNormalizer.StripLeadingArticle(NameNormalizer.Normalize("The Mistborn Saga")));
    }

    [TestMethod]
    public void StripLeadingArticle_NoLeadingThe_IsUnchanged()
    {
        var normalized = NameNormalizer.Normalize("Mistborn Saga");
        Assert.AreEqual(normalized, NameNormalizer.StripLeadingArticle(normalized));
    }

    [TestMethod]
    public void StripLeadingArticle_TheAsAWholeToken_IsNotStrippedFromTheMiddleOrWithoutTrailingSpace()
    {
        // "the" alone (no following token) must not be reduced to an empty string via a bad
        // prefix check, and "theory" must not be affected by a naive StartsWith("the").
        Assert.AreEqual("the", NameNormalizer.StripLeadingArticle("the"));
        Assert.AreEqual("theory", NameNormalizer.StripLeadingArticle("theory"));
    }

    [TestMethod]
    public void StripLeadingArticle_AuthorNamedTheRock_IsNotAffectedByAuthorNormalization()
    {
        // The helper itself is a pure string function usable by either kind - the series-only
        // guarantee lives in SimilarityGrouper's isSeries flag, not here. This just documents
        // that an author literally named "The Rock" strips down to "rock" if the helper were
        // (wrongly) applied to it - which is exactly why SimilarityGrouper must gate this call
        // on isSeries.
        Assert.AreEqual("rock", NameNormalizer.StripLeadingArticle(NameNormalizer.Normalize("The Rock")));
    }
}
