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

    [TestMethod]
    public void Build_MainAuthor_ReplacesTheWholeAuthorList()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "Brandon Sanderson", "Janci Patterson" }, "Steelheart", "file.m4b",
            SearchInitialsHandling.AsStored, mainAuthor: "Brandon Sanderson");

        Assert.AreEqual("Brandon Sanderson - Steelheart", query);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Build_BlankMainAuthor_KeepsEveryAuthor(string? mainAuthor)
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "A One", "B Two" }, "Book", "file.m4b", SearchInitialsHandling.AsStored, mainAuthor);

        Assert.AreEqual("A One, B Two - Book", query);
    }

    [TestMethod]
    public void Build_MainAuthor_GetsTheInitialsHandlingToo()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "George R. R. Martin", "Other Person" }, "A Game of Thrones", "file.m4b",
            SearchInitialsHandling.Compact, mainAuthor: "George R. R. Martin");

        Assert.AreEqual("George R.R. Martin - A Game of Thrones", query);
    }

    [TestMethod]
    public void Build_MainAuthorButNoBookName_FallsBackToTheFileName()
    {
        var query = MetadataSearchQueryBuilder.Build(
            new[] { "A One", "B Two" }, null, "file.m4b", SearchInitialsHandling.AsStored, mainAuthor: "A One");

        Assert.AreEqual("file.m4b", query);
    }

    [TestMethod]
    public void BuildPerSource_EachSourceUsesItsOwnHandling_OthersTheLibraryWideOne()
    {
        var settings = new LibrarySettings { SearchInitialsHandling = SearchInitialsHandling.AsStored };
        settings.SearchInitialsHandlingBySource["Hardcover"] = SearchInitialsHandling.Compact;
        settings.SearchInitialsHandlingBySource["Goodreads"] = SearchInitialsHandling.Spaced;

        var queries = MetadataSearchQueryBuilder.BuildPerSource(
            new[] { "Audible", "Hardcover", "Goodreads" }, new[] { "George R.R. Martin" }, "A Game of Thrones", "f.m4b", settings);

        Assert.AreEqual("George R.R. Martin - A Game of Thrones", queries["Hardcover"]);
        Assert.AreEqual("George R. R. Martin - A Game of Thrones", queries["Goodreads"]);
        Assert.AreEqual("George R.R. Martin - A Game of Thrones", queries["Audible"]);
        Assert.AreEqual(3, queries.Count);
    }

    [TestMethod]
    public void BuildPerSource_AppliesTheMainAuthorToEverySource()
    {
        var settings = new LibrarySettings();
        settings.SearchInitialsHandlingBySource["Hardcover"] = SearchInitialsHandling.Spaced;

        var queries = MetadataSearchQueryBuilder.BuildPerSource(
            new[] { "Audible", "Hardcover" }, new[] { "J.R.R. Tolkien", "Christopher Tolkien" }, "The Silmarillion", "f.m4b",
            settings, mainAuthor: "J.R.R. Tolkien");

        Assert.AreEqual("J.R.R. Tolkien - The Silmarillion", queries["Audible"]);
        Assert.AreEqual("J. R. R. Tolkien - The Silmarillion", queries["Hardcover"]);
    }

    [TestMethod]
    public void BuildPerSource_DuplicateSourceNames_AreCollapsed()
    {
        var queries = MetadataSearchQueryBuilder.BuildPerSource(
            new[] { "Audible", "audible" }, null, "Book", "f.m4b", new LibrarySettings());

        Assert.AreEqual(1, queries.Count);
    }
}
