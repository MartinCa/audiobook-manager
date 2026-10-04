using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using ScrapingPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

/// <summary>
/// Qualifiers reported by an online source are offered as their own refresh field, and only ever
/// add: a refresh never removes one the book has.
/// </summary>
[TestClass]
public class MetadataRefreshQualifiersTests
{
    private static Database.Models.Audiobook Book(params string[] qualifiers)
    {
        var book = new Database.Models.Audiobook(
            id: 1, bookName: "The Test Book", subtitle: null, series: null, seriesPart: null, year: 2010,
            description: null, copyright: null, publisher: null, language: "en", rating: null, asin: null,
            www: "https://www.audible.com/pd/test", coverFilePath: null, durationInSeconds: null,
            fileInfoFullPath: "/library/book.m4b", fileInfoFileName: "book.m4b", fileInfoSizeInBytes: 123)
        {
            Qualifiers = QualifierColumn.Serialize(qualifiers),
        };
        return book;
    }

    private static MetadataSearchResult Fetched(params string[] qualifiers) =>
        new("https://www.audible.com/pd/test", "The Test Book")
        {
            Source = "Audible",
            Authors = new List<ScrapingPerson>(),
            Narrators = new List<ScrapingPerson>(),
            Genres = new List<string>(),
            Qualifiers = qualifiers,
        };

    private static List<MetadataRefreshDiff> Diff(Database.Models.Audiobook book, MetadataSearchResult fetched) =>
        MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

    [TestMethod]
    public void Diff_ASourceQualifierTheBookLacks_IsOfferedAsTheSetItWouldHave()
    {
        var diff = Diff(Book(), Fetched("dramatized")).Single();

        Assert.AreEqual(MetadataRefreshFields.Qualifiers, diff.Field);
        Assert.IsNull(diff.LibraryValue);
        Assert.AreEqual("Dramatized", diff.SourceValue);
    }

    [TestMethod]
    public void Diff_TheProposedSetIsTheUnion_SoAnExistingQualifierIsNeverDropped()
    {
        var diff = Diff(Book("abridged"), Fetched("dramatized")).Single();

        Assert.AreEqual("Abridged", diff.LibraryValue);
        Assert.AreEqual("Abridged, Dramatized", diff.SourceValue);
    }

    [TestMethod]
    public void Diff_ASourceWithNoQualifiersNeverOffersToRemoveTheBooks()
    {
        Assert.AreEqual(0, Diff(Book("dramatized"), Fetched()).Count);
    }

    [TestMethod]
    public void Diff_ASourceQualifierTheBookAlreadyHas_IsNoDifference()
    {
        Assert.AreEqual(0, Diff(Book("dramatized"), Fetched("dramatized")).Count);
    }

    [TestMethod]
    public void DiffSnapshot_AgreesWithDiff_ForAFreshSnapshot()
    {
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched("dramatized"));

        var diff = MetadataRefreshDiffer
            .DiffSnapshot(Book(), snapshot, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted)
            .Single();

        Assert.AreEqual(MetadataRefreshFields.Qualifiers, diff.Field);
        Assert.AreEqual("Dramatized", diff.SourceValue);
    }

    [TestMethod]
    public void DiffSnapshot_ALegacySnapshotWithoutQualifiersOffersNoQualifierChange()
    {
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched()) with { Version = 4, Qualifiers = null };

        var diffs = MetadataRefreshDiffer
            .DiffSnapshot(Book("abridged"), snapshot, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted)
            .ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Snapshot_RoundTripsQualifiers_AndOmitsThemWhenThereAreNone()
    {
        var withQualifiers = PendingRefreshPayload.Serialize(PendingRefreshPayload.FromSearchResult(Fetched("dramatized", "abridged")));
        var without = PendingRefreshPayload.Serialize(PendingRefreshPayload.FromSearchResult(Fetched()));

        CollectionAssert.AreEqual(new[] { "abridged", "dramatized" }, PendingRefreshPayload.TryParse(withQualifiers)!.Qualifiers!.ToArray());
        Assert.IsFalse(without.Contains("qualifiers"), "a snapshot with none must serialize byte-identically to one written before the field existed");
        Assert.IsNull(PendingRefreshPayload.TryParse(without)!.Qualifiers);
    }

    [TestMethod]
    public void ApplyFields_Qualifiers_AddsToTheBooksAndKeepsWhatItHas()
    {
        var book = new AudiobookManager.Domain.Audiobook(
            new List<ScrapingPerson> { new("A") }, "The Test Book", 2010, new AudiobookFileInfo("/x.m4b", "x.m4b", 1))
        {
            Qualifiers = new List<string> { "abridged" },
        };
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched("dramatized"));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { MetadataRefreshFields.Qualifiers });

        CollectionAssert.AreEqual(new[] { "abridged", "dramatized" }, book.Qualifiers.ToArray());
    }

    [TestMethod]
    [DataRow(null, null, "en")]
    [DataRow(null, "da", "da")]
    [DataRow("Dansk", null, "da")]
    public void ApplyFields_Language_DefaultsToEnglishOnlyWhenNeitherSideHasOne(
        string? sourceLanguage, string? bookLanguage, string expected)
    {
        var book = new AudiobookManager.Domain.Audiobook(
            new List<ScrapingPerson> { new("A") }, "The Test Book", 2010, new AudiobookFileInfo("/x.m4b", "x.m4b", 1))
        {
            Language = bookLanguage,
        };
        var fetched = Fetched();
        fetched.Language = sourceLanguage;
        var snapshot = PendingRefreshPayload.FromSearchResult(fetched);

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { MetadataRefreshFields.Language });

        Assert.AreEqual(expected, book.Language);
    }

    [TestMethod]
    public void ApplyFields_WithoutTheQualifiersField_LeavesThemAlone()
    {
        var book = new AudiobookManager.Domain.Audiobook(
            new List<ScrapingPerson> { new("A") }, "The Test Book", 2010, new AudiobookFileInfo("/x.m4b", "x.m4b", 1));
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched("dramatized"));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { MetadataRefreshFields.BookName });

        Assert.AreEqual(0, book.Qualifiers.Count);
    }

    [TestMethod]
    public void ApplyFields_ASnapshotWithNoQualifiersNeverClearsTheBooks()
    {
        var book = new AudiobookManager.Domain.Audiobook(
            new List<ScrapingPerson> { new("A") }, "The Test Book", 2010, new AudiobookFileInfo("/x.m4b", "x.m4b", 1))
        {
            Qualifiers = new List<string> { "dramatized" },
        };
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched());

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { MetadataRefreshFields.Qualifiers });

        CollectionAssert.AreEqual(new[] { "dramatized" }, book.Qualifiers.ToArray());
    }
}
