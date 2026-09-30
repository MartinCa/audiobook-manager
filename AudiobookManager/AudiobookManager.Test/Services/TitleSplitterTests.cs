using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class TitleSplitterTests
{
    [TestMethod]
    public void Apply_SplitOnColonFalse_ReturnsTitleUnsplit()
    {
        var (bookName, subtitle) = TitleSplitter.Apply("The Hobbit: There and Back Again", null, splitOnColon: false);

        Assert.AreEqual("The Hobbit: There and Back Again", bookName);
        Assert.IsNull(subtitle);
    }

    [TestMethod]
    public void Apply_SplitOnColonTrue_GenuineTitleSubtitleSeparator_Splits()
    {
        var (bookName, subtitle) = TitleSplitter.Apply("The Hobbit: There and Back Again", null, splitOnColon: true);

        Assert.AreEqual("The Hobbit", bookName);
        Assert.AreEqual("There and Back Again", subtitle);
    }

    // Regression test: "4:50 from Paddington" (a train time, not a subtitle separator) must never
    // be split, even with the toggle on - a bare colon with no following space is not a
    // "Title: Subtitle" separator.
    [TestMethod]
    public void Apply_SplitOnColonTrue_BareColonWithNoFollowingSpace_DoesNotSplit()
    {
        var (bookName, subtitle) = TitleSplitter.Apply("4:50 from Paddington", null, splitOnColon: true);

        Assert.AreEqual("4:50 from Paddington", bookName);
        Assert.IsNull(subtitle);
    }

    [TestMethod]
    public void Apply_SplitOnColonTrue_SubtitleAlreadyPresent_NeverOverwritesIt()
    {
        var (bookName, subtitle) = TitleSplitter.Apply(
            "The Hobbit: There and Back Again", "An Unexpected Journey", splitOnColon: true);

        Assert.AreEqual("The Hobbit: There and Back Again", bookName);
        Assert.AreEqual("An Unexpected Journey", subtitle);
    }

    [TestMethod]
    public void Apply_SplitOnColonTrue_NoColonInTitle_ReturnsTitleUnchanged()
    {
        var (bookName, subtitle) = TitleSplitter.Apply("The Shining", null, splitOnColon: true);

        Assert.AreEqual("The Shining", bookName);
        Assert.IsNull(subtitle);
    }

    // Regression: a source (Hardcover) that returns both "Title: Subtitle" as the title and the
    // subtitle separately left the duplicated tail in the book name, so the toggle did nothing.
    [TestMethod]
    public void Apply_SplitOnColonTrue_SubtitleRepeatedInTitle_StripsDuplicatedTail()
    {
        var (bookName, subtitle) = TitleSplitter.Apply(
            "Agatha Christie: A Very Elusive Woman", "A Very Elusive Woman", splitOnColon: true);

        Assert.AreEqual("Agatha Christie", bookName);
        Assert.AreEqual("A Very Elusive Woman", subtitle);
    }
}
