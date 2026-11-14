using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;

/// <summary>
/// The "Only the main author in the folder name" library setting: the author folder names just the
/// book's main author, and a book without one (or whose main author is not among its authors) keeps
/// its full author list so the setting can never leave a book without an author folder.
/// </summary>
[TestClass]
public class MainAuthorInPathTests
{
    private static readonly char Sep = AudiobookFileHandler.GetDirectorySeparator();

    private static readonly LibraryPathOptions MainOnly = new(0, true);

    private static Audiobook MakeBook(string? mainAuthor, params string[] authors) =>
        new(authors.Select(a => new Person(a)).ToList(), "Title", 2020, new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1))
        {
            MainAuthor = mainAuthor,
        };

    [TestMethod]
    public void SettingOn_MainAuthorSet_AuthorFolderNamesOnlyTheMainAuthor()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(
            MakeBook("Brandon Sanderson", "Brandon Sanderson", "Janci Patterson"), MainOnly);

        Assert.AreEqual($"Brandon Sanderson{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOff_MainAuthorSet_AuthorFolderStillNamesEveryAuthor()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(
            MakeBook("Brandon Sanderson", "Brandon Sanderson", "Janci Patterson"), LibraryPathOptions.None);

        Assert.AreEqual($"Brandon Sanderson, Janci Patterson{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_NoMainAuthor_KeepsTheFullAuthorList()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(null, "A One", "B Two"), MainOnly);

        Assert.AreEqual($"A One, B Two{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_BlankMainAuthor_KeepsTheFullAuthorList()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("   ", "A One", "B Two"), MainOnly);

        Assert.AreEqual($"A One, B Two{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_MainAuthorNotAmongTheAuthors_IsIgnored()
    {
        // A stale name (the author was removed or renamed in the same edit) must never reach the
        // path: it would file the book under someone who is not credited on it.
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("Ghost Writer", "A One", "B Two"), MainOnly);

        Assert.AreEqual($"A One, B Two{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_MainAuthorIsNotTheFirstAuthor_StillUsedAlone()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("B Two", "A One", "B Two"), MainOnly);

        Assert.AreEqual($"B Two{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_MainAuthorMatchIsExact_NotCaseInsensitive()
    {
        // Persons are keyed by exact name, so "a one" and "A One" are different people.
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("a one", "A One", "B Two"), MainOnly);

        Assert.AreEqual($"A One, B Two{Sep}2020 - Title{Sep}2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_WorksTogetherWithTheNarratorSuffixAndSeries()
    {
        var book = MakeBook("A One", "A One", "B Two");
        book.Series = "Saga";
        book.SeriesPart = "1";
        book.Narrators = new List<Person> { new("Narrator A") };

        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(book, new LibraryPathOptions(1, true));

        Assert.AreEqual(
            $"A One{Sep}Saga{Sep}Book 01 - 2020 - Title {{Narrator A}}{Sep}Saga 01 - 2020 - Title.m4b", path);
    }

    [TestMethod]
    public void SettingOn_TwoBooksSharingAMainAuthorShareTheAuthorFolder()
    {
        var a = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("A One", "A One", "B Two"), MainOnly);
        var b = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook("A One", "A One", "C Three"), MainOnly);

        Assert.AreEqual(a.Split(Sep)[0], b.Split(Sep)[0]);
    }

    [TestMethod]
    public void SettingOn_OverlongMainAuthor_IsStillBoundedLikeAnyAuthorFolder()
    {
        var longName = new string('x', 400);

        var folder = AudiobookFileHandler.GetAuthorFolderName(MakeBook(longName, longName, "B Two"), mainAuthorOnly: true);

        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(folder) <= AudiobookFileHandler.MaxAuthorFolderBytes, folder.Length.ToString());
    }

    [TestMethod]
    public void EffectiveMainAuthor_IsNullUnlessItNamesOneOfTheAuthors()
    {
        Assert.AreEqual("A One", MakeBook("A One", "A One", "B Two").EffectiveMainAuthor);
        Assert.IsNull(MakeBook("Nobody", "A One", "B Two").EffectiveMainAuthor);
        Assert.IsNull(MakeBook(null, "A One").EffectiveMainAuthor);
        Assert.IsNull(MakeBook("", "A One").EffectiveMainAuthor);
    }
}
