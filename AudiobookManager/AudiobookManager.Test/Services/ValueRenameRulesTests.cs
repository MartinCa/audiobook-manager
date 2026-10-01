using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class ValueRenameRulesTests
{
    [TestMethod]
    public void ValidateAuthor_AValidRename_IsAccepted()
    {
        Assert.IsNull(ValueRenameRules.ValidateAuthor("Robert Galbraith", "J.K. Rowling"));
    }

    [TestMethod]
    [DataRow(null, "New")]
    [DataRow("  ", "New")]
    [DataRow("Old", null)]
    [DataRow("Old", "   ")]
    [DataRow("Old", "Old")]
    [DataRow("Old", "  Old ")]
    public void ValidateAuthor_BlankOrUnchanged_IsRefused(string? oldName, string? newName)
    {
        Assert.IsNotNull(ValueRenameRules.ValidateAuthor(oldName, newName));
    }

    [TestMethod]
    public void ValidateAuthor_ANameWithAComma_IsRefusedBecauseTheTagWouldReadItBackAsTwoAuthors()
    {
        var error = ValueRenameRules.ValidateAuthor("Old", "Sanderson, Brandon");

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "comma");
    }

    [TestMethod]
    public void ValidateSeries_AValidRename_IsAccepted()
    {
        Assert.IsNull(ValueRenameRules.ValidateSeries("Mistborn", "The Mistborn Saga"));
    }

    [TestMethod]
    [DataRow("Jack Reacher (Dramatized)")]
    [DataRow("Jack Reacher (Abridged)")]
    [DataRow("jack reacher (dramatized)")]
    public void ValidateSeries_ANameCarryingAQualifierSuffix_IsRefused(string newName)
    {
        // The qualifier is part of the book, not of the stored series: letting it in would double
        // the suffix on every qualified book of the series.
        var error = ValueRenameRules.ValidateSeries("Jack Reacher", newName);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "qualifier");
    }

    [TestMethod]
    public void ValidateSeries_ANameThatIsOnlyAQualifierWord_IsStillAcceptable()
    {
        // "(Abridged)" with nothing before it is not a clean name plus a suffix.
        Assert.IsNull(ValueRenameRules.ValidateSeries("Old", "(Abridged)"));
    }

    [TestMethod]
    public void ValidateSeries_ParenthesesThatAreNotAQualifier_AreAccepted()
    {
        Assert.IsNull(ValueRenameRules.ValidateSeries("Old", "Discworld (Death)"));
    }

    [TestMethod]
    public void ValidateSeries_ACommaIsFineInASeriesName()
    {
        Assert.IsNull(ValueRenameRules.ValidateSeries("Old", "Wheel of Time, The"));
    }
}
