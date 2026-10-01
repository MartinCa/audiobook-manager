using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class AuthorNameReviewTests
{
    private const InitialsSpacing Unspaced = InitialsSpacing.Unspaced;
    private const InitialsPunctuation Dotted = InitialsPunctuation.Dotted;

    [TestMethod]
    public void ProposeRename_SameName_ProposesNothing()
    {
        Assert.IsNull(AuthorNameReview.ProposeRename("Brandon Sanderson", "Brandon Sanderson", Unspaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_DifferentName_ProposesTheSourcesSpelling()
    {
        Assert.AreEqual(
            "Robert Jordan",
            AuthorNameReview.ProposeRename("James Rigney", "Robert Jordan", Unspaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_OnlyTheInitialsSpacingDiffers_ProposesNothingWhenTheLibraryConventionAbsorbsIt()
    {
        // The source spells the initials spaced; the library stores them unspaced. Both follow the
        // library's convention once formatted, so there is nothing to review.
        Assert.IsNull(AuthorNameReview.ProposeRename("J.K. Rowling", "J. K. Rowling", Unspaced, Dotted));
        Assert.IsNull(AuthorNameReview.ProposeRename("J.K. Rowling", "J.K. Rowling", Unspaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_SpacedConvention_AbsorbsTheOppositeSpacing()
    {
        Assert.IsNull(AuthorNameReview.ProposeRename("J. K. Rowling", "J.K. Rowling", InitialsSpacing.Spaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_UndottedConvention_AbsorbsDottedSource()
    {
        Assert.IsNull(AuthorNameReview.ProposeRename("JK Rowling", "J.K. Rowling", Unspaced, InitialsPunctuation.Undotted));
    }

    [TestMethod]
    public void ProposeRename_ProposedNameFollowsTheLibraryConvention_NotTheSources()
    {
        // The accepted name must not be one the initials-spacing consistency check would flag.
        Assert.AreEqual(
            "J.R.R. Tolkien",
            AuthorNameReview.ProposeRename("Tolkien", "J. R. R. Tolkien", Unspaced, Dotted));
        Assert.AreEqual(
            "J. R. R. Tolkien",
            AuthorNameReview.ProposeRename("Tolkien", "J.R.R. Tolkien", InitialsSpacing.Spaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_ADifferenceBeyondInitials_IsStillProposed()
    {
        Assert.AreEqual(
            "J.K. Galbraith",
            AuthorNameReview.ProposeRename("J.K. Rowling", "J. K. Galbraith", Unspaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_CaseAndAccentDifferences_AreRealDifferences()
    {
        Assert.AreEqual("René Goscinny", AuthorNameReview.ProposeRename("Rene Goscinny", "René Goscinny", Unspaced, Dotted));
        Assert.AreEqual("Brandon SANDERSON", AuthorNameReview.ProposeRename("Brandon Sanderson", "Brandon SANDERSON", Unspaced, Dotted));
    }

    [TestMethod]
    public void ProposeRename_WhitespaceOnlyDifference_ProposesNothing()
    {
        Assert.IsNull(AuthorNameReview.ProposeRename("Brandon  Sanderson", " Brandon Sanderson ", Unspaced, Dotted));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ProposeRename_ASourceThatReportsNoName_ProposesNothing(string? sourceName)
    {
        Assert.IsNull(AuthorNameReview.ProposeRename("Brandon Sanderson", sourceName, Unspaced, Dotted));
    }
}
