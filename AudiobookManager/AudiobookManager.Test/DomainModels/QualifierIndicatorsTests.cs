using AudiobookManager.Domain;

namespace AudiobookManager.Test.DomainModels;

[TestClass]
public class QualifierIndicatorsTests
{
    private static readonly QualifierIndicatorRule[] Rules =
    {
        new("Audible", "Dramatized Adaptation", "dramatized"),
        new("Audible", "Full-Cast Dramatized Adaptation", "dramatized"),
        new("Audible", "Dramatized", "dramatized"),
        new("Audible", "Abridged", "abridged"),
    };

    [TestMethod]
    [DataRow("A Frontier Christmas [Dramatized Adaptation]", "A Frontier Christmas", "dramatized")]
    [DataRow("House of Earth and Blood (Part 1 of 2) (Dramatized Adaptation)", "House of Earth and Blood (Part 1 of 2)", "dramatized")]
    [DataRow("Powerless (Full-Cast Dramatized Adaptation)", "Powerless", "dramatized")]
    [DataRow("The Three Musketeers (Dramatized)", "The Three Musketeers", "dramatized")]
    [DataRow("Nancy Drew: The Secret of the Old Clock (Abridged)", "Nancy Drew: The Secret of the Old Clock", "abridged")]
    public void Extract_TheBookNamesFromTheRequest_AreCleanedAndTagged(string title, string expectedTitle, string expectedKey)
    {
        var (cleaned, qualifiers) = QualifierIndicators.Extract(title, "Audible", Rules);

        Assert.AreEqual(expectedTitle, cleaned);
        CollectionAssert.AreEqual(new List<string> { expectedKey }, qualifiers);
    }

    [TestMethod]
    public void Extract_MatchesIgnoringCaseAndEitherBracketStyle()
    {
        var (cleaned, qualifiers) = QualifierIndicators.Extract("Dune [dramatized  ADAPTATION]", "audible", Rules);

        Assert.AreEqual("Dune", cleaned);
        CollectionAssert.AreEqual(new List<string> { "dramatized" }, qualifiers);
    }

    [TestMethod]
    public void Extract_TwoQualifiersComeBackInCanonicalOrder()
    {
        var (cleaned, qualifiers) = QualifierIndicators.Extract("Dune (Dramatized) [Abridged]", "Audible", Rules);

        Assert.AreEqual("Dune", cleaned);
        CollectionAssert.AreEqual(new List<string> { "abridged", "dramatized" }, qualifiers);
    }

    [TestMethod]
    public void Extract_OnlyAWholeGroupCounts_NotAWordInsideTheTitleOrAGroupWithOtherContent()
    {
        foreach (var title in new[] { "A Dramatized History of Rome", "Dune (Dramatized Edition)", "Dune (Abridged Version) extra" })
        {
            var (cleaned, qualifiers) = QualifierIndicators.Extract(title, "Audible", Rules);

            Assert.AreEqual(title, cleaned);
            Assert.AreEqual(0, qualifiers.Count, title);
        }
    }

    [TestMethod]
    public void Extract_RulesForAnotherSourceAreIgnored()
    {
        var (cleaned, qualifiers) = QualifierIndicators.Extract("Dune (Abridged)", "Goodreads", Rules);

        Assert.AreEqual("Dune (Abridged)", cleaned);
        Assert.AreEqual(0, qualifiers.Count);
    }

    [TestMethod]
    public void Extract_ATitleThatIsOnlyAnIndicatorKeepsItsName()
    {
        var (cleaned, qualifiers) = QualifierIndicators.Extract("(Abridged)", "Audible", Rules);

        Assert.AreEqual("(Abridged)", cleaned);
        Assert.AreEqual(0, qualifiers.Count);
    }

    [TestMethod]
    public void Extract_NoRules_LeavesTheTitleAlone()
    {
        var (cleaned, qualifiers) = QualifierIndicators.Extract("Dune (Abridged)", "Audible", Array.Empty<QualifierIndicatorRule>());

        Assert.AreEqual("Dune (Abridged)", cleaned);
        Assert.AreEqual(0, qualifiers.Count);
    }

    [TestMethod]
    [DataRow("[Dramatized Adaptation]", "Dramatized Adaptation")]
    [DataRow("(Dramatized Adaptation)", "Dramatized Adaptation")]
    [DataRow("  Dramatized   Adaptation ", "Dramatized Adaptation")]
    [DataRow("()", "")]
    [DataRow("   ", "")]
    [DataRow(null, "")]
    public void NormalizeIndicator_StripsSurroundingBracketsAndCollapsesWhitespace(string? input, string expected)
    {
        Assert.AreEqual(expected, QualifierIndicators.NormalizeIndicator(input));
    }
}
