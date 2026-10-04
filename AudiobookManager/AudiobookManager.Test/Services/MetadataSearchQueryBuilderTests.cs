using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataSearchQueryBuilderTests
{
    [TestMethod]
    public void Build_AuthorAndBookNamePresent_ReturnsAuthorDashBookName()
    {
        var query = MetadataSearchQueryBuilder.Build(new[] { "Brandon Sanderson" }, "The Way of Kings", "file.m4b");

        Assert.AreEqual("Brandon Sanderson - The Way of Kings", query);
    }

    [TestMethod]
    public void Build_MultipleAuthors_JoinsThemWithComma()
    {
        var query = MetadataSearchQueryBuilder.Build(new[] { "Author One", "Author Two" }, "Book Title", "file.m4b");

        Assert.AreEqual("Author One, Author Two - Book Title", query);
    }

    [TestMethod]
    public void Build_NoAuthors_FallsBackToBookNameAlone()
    {
        var query = MetadataSearchQueryBuilder.Build(Array.Empty<string>(), "The Way of Kings", "file.m4b");

        Assert.AreEqual("The Way of Kings", query);
    }

    [TestMethod]
    public void Build_AuthorsOnlyBlank_FallsBackToBookNameAlone()
    {
        var query = MetadataSearchQueryBuilder.Build(new[] { "  ", "" }, "The Way of Kings", "file.m4b");

        Assert.AreEqual("The Way of Kings", query);
    }

    [TestMethod]
    public void Build_BookNameBlankEvenWithAuthor_FallsBackToFileName()
    {
        var query = MetadataSearchQueryBuilder.Build(new[] { "Author" }, "   ", "some-file.m4b");

        Assert.AreEqual("some-file.m4b", query);
    }

    [TestMethod]
    public void Build_NoAuthorsOrBookName_FallsBackToFileName()
    {
        var query = MetadataSearchQueryBuilder.Build(Array.Empty<string>(), "", "some-file.m4b");

        Assert.AreEqual("some-file.m4b", query);
    }

    [TestMethod]
    public void Build_NothingAvailable_ReturnsEmptyString()
    {
        var query = MetadataSearchQueryBuilder.Build(Array.Empty<string>(), null, null);

        Assert.AreEqual(string.Empty, query);
    }

    // Regression test: authorNames used to be passed straight into .Select() with no null guard,
    // so a caller unable to guarantee a non-null list (unlike the current caller, whose EF
    // Include always yields one) would throw ArgumentNullException instead of falling back.
    [TestMethod]
    public void Build_AuthorNamesIsNull_FallsBackToBookNameAlone()
    {
        var query = MetadataSearchQueryBuilder.Build(null, "The Way of Kings", "file.m4b");

        Assert.AreEqual("The Way of Kings", query);
    }

    [TestMethod]
    public void Build_AuthorNamesIsNullAndBookNameBlank_FallsBackToFileName()
    {
        var query = MetadataSearchQueryBuilder.Build(null, null, "some-file.m4b");

        Assert.AreEqual("some-file.m4b", query);
    }

    [TestMethod]
    [DataRow("George R. R. Martin", "George R.R. Martin")]
    [DataRow("George r. r. martin", "George r.r. martin")]
    [DataRow("George R R Martin", "George R.R. Martin")]
    [DataRow("George R.R. Martin", "George R.R. Martin")]
    [DataRow("Brandon Sanderson", "Brandon Sanderson")]
    public void FormatInitials_Compact_DottedWithNoSpaceBetweenInitials(string author, string expected)
    {
        Assert.AreEqual(expected, MetadataSearchQueryBuilder.FormatInitials(author, SearchInitialsHandling.Compact));
    }

    [TestMethod]
    [DataRow("George R.R. Martin", "George R. R. Martin")]
    [DataRow("George R R Martin", "George R. R. Martin")]
    [DataRow("J.K. Rowling", "J. K. Rowling")]
    [DataRow("Brandon Sanderson", "Brandon Sanderson")]
    public void FormatInitials_Spaced_DottedWithASpaceBetweenInitials(string author, string expected)
    {
        Assert.AreEqual(expected, MetadataSearchQueryBuilder.FormatInitials(author, SearchInitialsHandling.Spaced));
    }

    [TestMethod]
    public void FormatInitials_AsStored_ReturnsTheNameUntouched()
    {
        Assert.AreEqual("George R. R.  Martin", MetadataSearchQueryBuilder.FormatInitials("George R. R.  Martin", SearchInitialsHandling.AsStored));
    }

    [TestMethod]
    public void Build_DefaultsToAsStored()
    {
        var query = MetadataSearchQueryBuilder.Build(new[] { "George R. R. Martin" }, "A Knight of the Seven Kingdoms", "file.m4b");

        Assert.AreEqual("George R. R. Martin - A Knight of the Seven Kingdoms", query);
    }

    [TestMethod]
    public void Build_Compact_ReWritesAuthorInitialsButNotTheTitle()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "George R. R. Martin" }, "A B C of Things", "file.m4b", SearchInitialsHandling.Compact);

        Assert.AreEqual("George R.R. Martin - A B C of Things", query);
    }

    [TestMethod]
    public void Build_Compact_AppliesToEveryAuthor()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "J. R. R. Tolkien", "C S Lewis" }, "Book", null, SearchInitialsHandling.Compact);

        Assert.AreEqual("J.R.R. Tolkien, C.S. Lewis - Book", query);
    }

    [TestMethod]
    public void Build_Spaced_ReWritesAuthorInitials()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "George R.R. Martin" }, "Book", null, SearchInitialsHandling.Spaced);

        Assert.AreEqual("George R. R. Martin - Book", query);
    }

    [TestMethod]
    public void Build_Compact_LeavesFileNameFallbackUntouched()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "George R. R. Martin" }, "", "A B file.m4b", SearchInitialsHandling.Compact);

        Assert.AreEqual("A B file.m4b", query);
    }
}
