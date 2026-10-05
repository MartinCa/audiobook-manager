using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;

/// <summary>
/// The "Include narrator in folder name" library setting: the book folder gains an Audiobookshelf
/// style <c>{Narrator}</c> suffix so two narrations of one book get distinct folders.
/// </summary>
[TestClass]
public class NarratorInPathTests
{
    private static readonly char Sep = AudiobookFileHandler.GetDirectorySeparator();

    private static Audiobook MakeBook(string? series = null, string? part = null, params string[] narrators) =>
        new(new List<Person> { new("Author") }, "Title", 2020, new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1))
        {
            Series = series,
            SeriesPart = part,
            Narrators = narrators.Select(n => new Person(n)).ToList(),
        };

    [TestMethod]
    public void SettingOff_NarratorNeverAppearsInThePath()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), 0);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_AppendsTheNarratorInBracesToTheBookFolderOnly()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), 10);

        Assert.AreEqual($"Author{Sep}2020 - Title {{Narrator A}}{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_InASeries_AppendsToTheBookFolderAfterTheTitle()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("Saga", "1", "Narrator A"), 10);

        Assert.AreEqual(
            $"Author{Sep}Saga{Sep}Book 01 - 2020 - Title {{Narrator A}}{Sep}Saga 01 - 2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_QualifierSuffixStaysBeforeTheNarrator()
    {
        var book = MakeBook(null, null, "Narrator A");
        book.Qualifiers = new List<string> { "abridged" };

        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(book, 10);

        Assert.IsTrue(path.Contains("2020 - Title (Abridged) {Narrator A}"), path);
    }

    [TestMethod]
    public void SettingOn_NoNarrator_AddsNoEmptyBraces()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(), 10);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_BlankNarratorNames_AreIgnored()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "  ", ""), 10);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_SeveralNarrators_AreJoinedLikeTheNarratorTag()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "A One", "B Two", "A One"), 10);

        Assert.IsTrue(path.Contains("2020 - Title {A One, B Two}"), path);
    }

    [TestMethod]
    public void SettingOn_TwoNarrationsOfOneBook_GetDifferentFolders()
    {
        var a = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), 10);
        var b = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator B"), 10);
        var withoutSetting = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), 0);
        var withoutSettingB = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator B"), 0);

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(withoutSetting, withoutSettingB, "Off, the two collide - which is why the setting exists.");
    }

    [TestMethod]
    public void SettingOn_BracesInTheNarratorName_CannotEndTheGroupEarly()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Evil} Name{"), 10);

        Assert.IsTrue(path.Contains("2020 - Title {Evil Name}"), path);
    }

    [TestMethod]
    public void SettingOn_PathSeparatorInTheNarratorName_StaysInsideOneSegment()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "../../Escape"), 10);

        Assert.AreEqual(3, path.Split(Sep).Length, path);
    }

    [TestMethod]
    public void SettingOn_ALongCastList_IsCappedAtWholeNamesWithinTheByteBudget()
    {
        var narrators = Enumerable.Range(0, 40).Select(i => $"Narrator Number {i}").ToArray();

        var suffix = AudiobookFileHandler.GetNarratorFolderSuffix(MakeBook(null, null, narrators), 10);

        // " {" + group + "}" - the group is the first names that fit 100 bytes, never a partial one.
        var group = suffix[2..^1];
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(group) <= 100, group);
        Assert.IsTrue(narrators.Take(group.Split(", ").Length).SequenceEqual(group.Split(", ")), group);
        Assert.IsTrue(group.Split(", ").Length < narrators.Length);
    }

    [TestMethod]
    public void SettingOn_MultiByteCast_IsCappedInBytesNotCharacters()
    {
        // 60 two-byte characters is 120 bytes: under any character cap, over the byte budget.
        var name = new string('é', 60);

        var suffix = AudiobookFileHandler.GetNarratorFolderSuffix(MakeBook(null, null, name), 10);

        var group = suffix[2..^1];
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(group) <= 100, $"{group.Length} chars");
        Assert.IsTrue(group.Length > 0);
    }

    [TestMethod]
    public void SettingOn_ASingleNameCutOnTheBoundary_NeverEndsInALoneSurrogate()
    {
        // 4-byte characters: 100 bytes is exactly 25 of them, so a cut at 24.5 must drop the half.
        var name = string.Concat(Enumerable.Repeat("\U0001F3A7", 30));

        var suffix = AudiobookFileHandler.GetNarratorFolderSuffix(MakeBook(null, null, name), 10);

        var group = suffix[2..^1];
        // A lone surrogate does not survive a UTF-8 round trip (it comes back as U+FFFD).
        var roundTripped = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(group));
        Assert.AreEqual(group, roundTripped);
        Assert.AreEqual(25, group.EnumerateRunes().Count());
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(group) <= 100);
        Assert.IsFalse(group.Contains('\uFFFD'));
    }

    [TestMethod]
    public void SettingOn_AnOverLongNameInTheMiddle_IsSkippedAndLaterShortNamesAreStillKept()
    {
        var tooLong = new string('x', 95);

        var suffix = AudiobookFileHandler.GetNarratorFolderSuffix(MakeBook(null, null, "A One", tooLong, "B Two"), 10);

        Assert.AreEqual(" {A One, B Two}", suffix);
    }

    [TestMethod]
    public void SettingOn_AnOverLongFirstName_IsCutToTheBudgetAndNothingFollowsIt()
    {
        var tooLong = new string('x', 150);

        var suffix = AudiobookFileHandler.GetNarratorFolderSuffix(MakeBook(null, null, tooLong, "B Two"), 10);

        Assert.AreEqual($" {{{new string('x', 100)}}}", suffix);
    }

    [TestMethod]
    public void MaxNarrators_ListsOnlyTheFirstOnesInTagOrder()
    {
        var book = MakeBook(null, null, "A One", "B Two", "C Three");

        Assert.AreEqual(" {A One}", AudiobookFileHandler.GetNarratorFolderSuffix(book, 1));
        Assert.AreEqual(" {A One, B Two}", AudiobookFileHandler.GetNarratorFolderSuffix(book, 2));
        Assert.AreEqual(" {A One, B Two, C Three}", AudiobookFileHandler.GetNarratorFolderSuffix(book, 3));
        Assert.AreEqual(" {A One, B Two, C Three}", AudiobookFileHandler.GetNarratorFolderSuffix(book, 10));
    }

    [TestMethod]
    public void MaxNarrators_ZeroOrLess_ListsNoneEvenWhenTheBookHasNarrators()
    {
        var book = MakeBook(null, null, "A One");

        Assert.AreEqual("", AudiobookFileHandler.GetNarratorFolderSuffix(book, 0));
        Assert.AreEqual("", AudiobookFileHandler.GetNarratorFolderSuffix(book, -1));
    }

    [TestMethod]
    public void MaxNarrators_CountsDistinctNonBlankNames_NotRawEntries()
    {
        // Two entries are blank or repeated: the limit of 2 must still reach both real narrators.
        var book = MakeBook(null, null, "", "A One", "A One", "  ", "B Two", "C Three");

        Assert.AreEqual(" {A One, B Two}", AudiobookFileHandler.GetNarratorFolderSuffix(book, 2));
    }

    [TestMethod]
    public void MaxNarrators_ZeroInThePath_MeansTheNarratorIsNotInThePathAtAll()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "A One"), 0);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void MaxNarrators_TwoCastsSharingTheirFirstNarrator_CollideWhenTheLimitIsOne()
    {
        var a = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "A One", "B Two"), 1);
        var b = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "A One", "C Three"), 1);
        var wider = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "A One", "C Three"), 2);

        Assert.AreEqual(a, b, "The limit is a trade-off: a lower limit can make two casts share a folder.");
        Assert.AreNotEqual(a, wider);
    }

    [TestMethod]
    public void SettingOn_DuplicateNarrators_AreListedOnce()
    {
        var suffix = AudiobookFileHandler.GetNarratorFolderSuffix(MakeBook(null, null, "A One", "A One", "B Two"), 10);

        Assert.AreEqual(" {A One, B Two}", suffix);
    }

    [TestMethod]
    public void SettingOn_TheBraceGroupIsLastInTheFolderName_SoAudiobookshelfsFolderPatternMatchesIt()
    {
        // Audiobookshelf reads the narrator with /^(?<title>.*) \{(?<narrators>.*)\}$/ on the folder name.
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("Saga", "1", "A One", "B Two"), 10);

        var folder = path.Split(Sep)[2];
        var match = System.Text.RegularExpressions.Regex.Match(folder, @"^(?<title>.*) \{(?<narrators>.*)\}$");
        Assert.IsTrue(match.Success, folder);
        Assert.AreEqual("A One, B Two", match.Groups["narrators"].Value);
    }

    [TestMethod]
    public void SettingOn_ResultStillResolvesInsideTheLibraryRoot()
    {
        var relative = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), 10);

        var full = AudiobookFileHandler.JoinLibraryPath("/library", relative);

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(full, "/library"));
    }
}
