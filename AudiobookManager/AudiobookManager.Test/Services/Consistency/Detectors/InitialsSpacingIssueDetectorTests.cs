using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using DbPerson = AudiobookManager.Database.Models.Person;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DomainInitialsSpacing = AudiobookManager.Domain.InitialsSpacing;

namespace AudiobookManager.Test.Services.Consistency.Detectors;

[TestClass]
public class InitialsSpacingIssueDetectorTests
{
    private static DbAudiobook Book(long id, string bookName, params string[] authorNames) =>
        new(id, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/tmp/{id}.m4b", $"{id}.m4b", 1000)
        {
            Authors = authorNames.Select(name => new DbPerson(default, name)).ToList()
        };

    private static DbAudiobook NarratedBook(long id, string bookName, string narratorName) =>
        new(id, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/tmp/{id}.m4b", $"{id}.m4b", 1000)
        {
            Narrators = new List<DbPerson> { new(default, narratorName) }
        };

    [TestMethod]
    public void Detect_YieldsOneIssuePerNonCompliantPersonValue_NotPerBook()
    {
        // "J. K. Rowling" (spaced) appears on three books under Unspaced; "George R.R. Martin"
        // (unspaced) is already compliant under Unspaced.
        var books = new List<DbAudiobook>
        {
            Book(1, "Philosopher's Stone", "J. K. Rowling"),
            Book(2, "Chamber of Secrets", "J. K. Rowling"),
            Book(3, "Prisoner of Azkaban", "J. K. Rowling"),
            Book(4, "A Game of Thrones", "George R.R. Martin"),
        };

        var issues = new InitialsSpacingIssueDetector()
            .Detect(books, DomainInitialsSpacing.Unspaced)
            .ToList();

        Assert.AreEqual(1, issues.Count, "one issue per distinct person value, not per book");
        var issue = issues[0];
        Assert.AreEqual(ConsistencyIssueType.InitialsSpacingMismatch, issue.IssueType);
        Assert.AreEqual("J. K. Rowling", issue.ActualValue);
        Assert.AreEqual("J.K. Rowling", issue.ExpectedValue);
        StringAssert.Contains(issue.Description, "3 books");
        Assert.AreEqual(1, issue.AudiobookId, "representative book is the first book carrying the value");
    }

    [TestMethod]
    public void Detect_MapsExpectedValueBackToCanonicalUnderTheSetting()
    {
        // Stored value is unspaced, setting says Spaced => canonical is the spaced form.
        var books = new List<DbAudiobook> { Book(7, "The Hobbit", "J.R.R. Tolkien") };

        var issue = new InitialsSpacingIssueDetector()
            .Detect(books, DomainInitialsSpacing.Spaced)
            .Single();

        Assert.AreEqual("J.R.R. Tolkien", issue.ActualValue);
        Assert.AreEqual("J. R. R. Tolkien", issue.ExpectedValue);
    }

    [TestMethod]
    public void Detect_ChecksNarratorsToo()
    {
        var books = new List<DbAudiobook>
        {
            NarratedBook(9, "A Study in Drowning", "S. A. Chakraborty")
        };

        var issue = new InitialsSpacingIssueDetector()
            .Detect(books, DomainInitialsSpacing.Unspaced)
            .Single();

        Assert.AreEqual("S. A. Chakraborty", issue.ActualValue);
        Assert.AreEqual("S.A. Chakraborty", issue.ExpectedValue);
    }

    [TestMethod]
    public void Detect_PersonOnSeveralBooks_CountsOncePerBookAcrossRoles()
    {
        // Same person authored AND narrated one book: counted once for that book.
        var book = Book(11, "Self-Narrated", "A. B. Author");
        book.Narrators = new List<Person> { new(default, "A. B. Author") };

        var issue = new InitialsSpacingIssueDetector()
            .Detect(new[] { book }, DomainInitialsSpacing.Unspaced)
            .Single();

        StringAssert.Contains(issue.Description, "1 book");
    }

    [TestMethod]
    public void Detect_AllCompliant_EmitsNothing()
    {
        var books = new List<DbAudiobook>
        {
            Book(1, "Book", "J.K. Rowling", "Stephen King"),
            Book(2, "Book 2", "Brandon Sanderson")
        };

        var issues = new InitialsSpacingIssueDetector().Detect(books, DomainInitialsSpacing.Unspaced).ToList();
        Assert.AreEqual(0, issues.Count);
    }

    [TestMethod]
    public void Detect_IssuesAreOrderedByName_SoRepeatedChecksAreStable()
    {
        var books = new List<DbAudiobook>
        {
            Book(1, "B", "G. R. R. Martin"),
            Book(2, "A", "J. K. Rowling"),
            Book(3, "C", "A. A. Milne")
        };

        var issues = new InitialsSpacingIssueDetector().Detect(books, DomainInitialsSpacing.Unspaced).ToList();

        CollectionAssert.AreEqual(
            new[] { "A. A. Milne", "G. R. R. Martin", "J. K. Rowling" },
            issues.Select(i => i.ActualValue).ToArray());
    }
}