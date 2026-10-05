using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainPerson = AudiobookManager.Domain.Person;
using DomainFileInfo = AudiobookManager.Domain.AudiobookFileInfo;

namespace AudiobookManager.Test.Services.Consistency.Detectors;

/// <summary>
/// The consistency check has to agree with the organize path about where a book belongs, so a
/// toggled "Include narrator in folder name" setting shows up as WrongFilePath and the resolve
/// moves the file to exactly the path the check expected.
/// </summary>
[TestClass]
public class PathMismatchDetectorNarratorTests
{
    private static readonly string Sep = AudiobookManager.FileManager.AudiobookFileHandler.GetDirectorySeparator().ToString();

    private static DbAudiobook MakeDbBook(string fullPath) =>
        new(1, "Title", null, null, null, 2020,
            null, null, null, null, null, null, null, null, null,
            fullPath, "book.m4b", 1000)
        {
            Authors = new List<Person> { new(1, "Author") },
        };

    private static DomainAudiobook MakeParsed() =>
        new(new List<DomainPerson> { new("Author") }, "Title", 2020,
            new DomainFileInfo("/library/x/book.m4b", "book.m4b", 1000))
        {
            Narrators = new List<DomainPerson> { new("Narrator A") },
        };

    private static List<BookConsistencyIssue> Run(string storedPath, bool includeNarrator) =>
        new PathMismatchDetector().Detect(new AudiobookCheckContext(
            MakeDbBook(storedPath), MakeParsed(), "/library", "/library", includeNarrator ? 3 : 0)).ToList();

    [TestMethod]
    public void SettingOff_BookWithoutNarratorFolder_IsInPlace()
    {
        var issues = Run("/library/Author/2020 - Title/2020 - Title.m4b".Replace("/", Sep), false);

        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void SettingOn_BookWithoutNarratorFolder_IsFlaggedAndExpectedPathCarriesTheNarrator()
    {
        var issue = Run("/library/Author/2020 - Title/2020 - Title.m4b".Replace("/", Sep), true).Single();

        Assert.AreEqual(BookConsistencyIssueType.WrongFilePath, issue.IssueType);
        var expected = "/library/Author/2020 - Title {Narrator A}/2020 - Title.m4b".Replace("/", Sep);
        Assert.AreEqual(expected, issue.ExpectedValue);
    }

    [TestMethod]
    public void SettingOn_BookAlreadyInNarratorFolder_IsInPlace()
    {
        var issues = Run("/library/Author/2020 - Title {Narrator A}/2020 - Title.m4b".Replace("/", Sep), true);

        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void SettingOff_BookInNarratorFolder_IsFlagged()
    {
        var issue = Run("/library/Author/2020 - Title {Narrator A}/2020 - Title.m4b".Replace("/", Sep), false).Single();

        Assert.AreEqual(BookConsistencyIssueType.WrongFilePath, issue.IssueType);
    }
}
