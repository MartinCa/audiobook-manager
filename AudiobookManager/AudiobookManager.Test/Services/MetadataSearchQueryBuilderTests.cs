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
}
