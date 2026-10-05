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
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), false);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_AppendsTheNarratorInBracesToTheBookFolderOnly()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), true);

        Assert.AreEqual($"Author{Sep}2020 - Title {{Narrator A}}{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_InASeries_AppendsToTheBookFolderAfterTheTitle()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("Saga", "1", "Narrator A"), true);

        Assert.AreEqual(
            $"Author{Sep}Saga{Sep}Book 01 - 2020 - Title {{Narrator A}}{Sep}Saga 01 - 2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_QualifierSuffixStaysBeforeTheNarrator()
    {
        var book = MakeBook(null, null, "Narrator A");
        book.Qualifiers = new List<string> { "abridged" };

        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(book, true);

        Assert.IsTrue(path.Contains("2020 - Title (Abridged) {Narrator A}"), path);
    }

    [TestMethod]
    public void SettingOn_NoNarrator_AddsNoEmptyBraces()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(), true);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_BlankNarratorNames_AreIgnored()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "  ", ""), true);

        Assert.AreEqual($"Author{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_SeveralNarrators_AreJoinedLikeTheNarratorTag()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "A One", "B Two", "A One"), true);

        Assert.IsTrue(path.Contains("2020 - Title {A One, B Two}"), path);
    }

    [TestMethod]
    public void SettingOn_TwoNarrationsOfOneBook_GetDifferentFolders()
    {
        var a = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), true);
        var b = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator B"), true);
        var withoutSetting = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), false);
        var withoutSettingB = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator B"), false);

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(withoutSetting, withoutSettingB, "Off, the two collide - which is why the setting exists.");
    }

    [TestMethod]
    public void SettingOn_BracesInTheNarratorName_CannotEndTheGroupEarly()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Evil} Name{"), true);

        Assert.IsTrue(path.Contains("2020 - Title {Evil Name}"), path);
    }

    [TestMethod]
    public void SettingOn_PathSeparatorInTheNarratorName_StaysInsideOneSegment()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "../../Escape"), true);

        Assert.AreEqual(3, path.Split(Sep).Length, path);
    }

    [TestMethod]
    public void SettingOn_ALongCastList_IsCappedSoTheFolderNameStaysUsable()
    {
        var narrators = Enumerable.Range(0, 40).Select(i => $"Narrator Number {i}").ToArray();

        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, narrators), true);

        var folder = path.Split(Sep)[1];
        Assert.IsTrue(folder.Length < 140, $"Folder name is {folder.Length} characters: {folder}");
        Assert.IsTrue(folder.EndsWith('}'));
    }

    [TestMethod]
    public void SettingOn_ResultStillResolvesInsideTheLibraryRoot()
    {
        var relative = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, null, "Narrator A"), true);

        var full = AudiobookFileHandler.JoinLibraryPath("/library", relative);

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(full, "/library"));
    }
}
