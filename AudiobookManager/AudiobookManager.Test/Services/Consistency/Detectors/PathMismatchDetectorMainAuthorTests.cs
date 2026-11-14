using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainPerson = AudiobookManager.Domain.Person;
using DomainFileInfo = AudiobookManager.Domain.AudiobookFileInfo;

namespace AudiobookManager.Test.Services.Consistency.Detectors;

/// <summary>
/// The consistency check has to agree with the organize path about where a book belongs, so a
/// toggled "Only the main author in folder name" setting shows up as WrongFilePath, and the expected
/// path is the one the resolve will move the file to.
/// </summary>
[TestClass]
public class PathMismatchDetectorMainAuthorTests
{
    private static readonly string Sep = AudiobookManager.FileManager.AudiobookFileHandler.GetDirectorySeparator().ToString();

    private static DbAudiobook MakeDbBook(string fullPath) =>
        new(1, "Title", null, null, null, 2020,
            null, null, null, null, null, null, null, null, null,
            fullPath, "book.m4b", 1000)
        {
            Authors = new List<Person> { new(1, "A One"), new(2, "B Two") },
        };

    private static DomainAudiobook MakeParsed(string? mainAuthor) =>
        new(new List<DomainPerson> { new("A One"), new("B Two") }, "Title", 2020,
            new DomainFileInfo("/library/x/book.m4b", "book.m4b", 1000))
        {
            MainAuthor = mainAuthor,
        };

    private static List<BookConsistencyIssue> Run(string storedPath, string? mainAuthor, bool mainAuthorOnly) =>
        new PathMismatchDetector().Detect(new AudiobookCheckContext(
            MakeDbBook(storedPath), MakeParsed(mainAuthor), "/library", "/library",
            new AudiobookManager.Domain.LibraryPathOptions(0, mainAuthorOnly))).ToList();

    [TestMethod]
    public void SettingOff_BookInTheFullAuthorFolder_IsInPlace()
    {
        var issues = Run("/library/A One, B Two/2020 - Title/2020 - Title.m4b".Replace("/", Sep), "A One", false);

        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void SettingOn_BookInTheFullAuthorFolder_IsFlaggedAndExpectedPathHasTheMainAuthorAlone()
    {
        var issue = Run("/library/A One, B Two/2020 - Title/2020 - Title.m4b".Replace("/", Sep), "A One", true).Single();

        Assert.AreEqual(BookConsistencyIssueType.WrongFilePath, issue.IssueType);
        var expected = "/library/A One/2020 - Title/2020 - Title.m4b".Replace("/", Sep);
        Assert.AreEqual(expected, issue.ExpectedValue);
    }

    [TestMethod]
    public void SettingOn_BookAlreadyUnderTheMainAuthor_IsInPlace()
    {
        var issues = Run("/library/A One/2020 - Title/2020 - Title.m4b".Replace("/", Sep), "A One", true);

        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void SettingOn_BookWithoutAMainAuthor_IsNotFlaggedForItsFullAuthorFolder()
    {
        var issues = Run("/library/A One, B Two/2020 - Title/2020 - Title.m4b".Replace("/", Sep), null, true);

        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void SettingOff_BookUnderTheMainAuthorAlone_IsFlagged()
    {
        var issue = Run("/library/A One/2020 - Title/2020 - Title.m4b".Replace("/", Sep), "A One", false).Single();

        Assert.AreEqual(BookConsistencyIssueType.WrongFilePath, issue.IssueType);
        var expected = "/library/A One, B Two/2020 - Title/2020 - Title.m4b".Replace("/", Sep);
        Assert.AreEqual(expected, issue.ExpectedValue);
    }
}
