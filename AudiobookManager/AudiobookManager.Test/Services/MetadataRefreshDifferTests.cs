using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using AudiobookManager.Scraping.Models;
using ScrapingPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataRefreshDifferTests
{
    private static Database.Models.Audiobook Book(
        string? bookName = null, string? subtitle = null, string? series = null,
        string? seriesPart = null, int year = 2010, string? language = "en",
        string? rating = null, string? description = null)
    {
        var book = new Database.Models.Audiobook(
            id: 1,
            bookName: bookName ?? "The Test Book",
            subtitle: subtitle,
            series: series,
            seriesPart: seriesPart,
            year: year,
            description: description,
            copyright: null,
            publisher: null,
            language: language,
            rating: rating,
            asin: null,
            www: null,
            coverFilePath: null,
            durationInSeconds: null,
            fileInfoFullPath: "/library/book.m4b",
            fileInfoFileName: "book.m4b",
            fileInfoSizeInBytes: 123);
        return book;
    }

    private static MetadataSearchResult Fetched(Action<MetadataSearchResult>? configure = null)
    {
        var result = new MetadataSearchResult("https://www.audible.com/pd/test", "The Test Book")
        {
            Source = "Audible",
            Authors = new List<ScrapingPerson>(),
            Narrators = new List<ScrapingPerson>(),
            Genres = new List<string>(),
        };
        configure?.Invoke(result);
        return result;
    }

    [TestMethod]
    public void Diff_NoChanges_ProducesNoDifferences()
    {
        var book = Book();
        var fetched = Fetched();

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_ChangedBookName_ProducesSingleDiff()
    {
        var book = Book();
        var fetched = Fetched(r => r.BookName = "The Test Book (Unabridged)");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("BookName", diffs[0].Field);
        Assert.AreEqual("The Test Book", diffs[0].LibraryValue);
        Assert.AreEqual("The Test Book (Unabridged)", diffs[0].SourceValue);
    }

    [TestMethod]
    public void Diff_AuthorListReordered_ProducesNoDiff()
    {
        // Order is presentation, not data: the same set of names in a different order is not a
        // change a refresh should offer.
        var book = Book();
        book.Authors = new List<Database.Models.Person>
        {
            new(1, "Author One"),
            new(2, "Author Two"),
        };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson>
            {
                new("Author Two"),
                new("Author One"),
            };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_DuplicateNamesCollapsed_NoDiffFromRepetition()
    {
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "Author One") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson>
            {
                new("Author One"),
                new("Author One"),
            };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_WhitespaceOnlyDifferences_AreNotDifferences()
    {
        var book = Book(bookName: "The Test Book", description: "Some description.");
        var fetched = Fetched(r =>
        {
            r.BookName = "  The Test Book  ";
            r.Description = " Some description. ";
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_EmptyVsNull_IsNotADifference()
    {
        var book = Book(subtitle: "");
        var fetched = Fetched(r => r.Subtitle = null);

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_SourceLacksYear_DoesNotOfferBlankingYear()
    {
        var book = Book(year: 2010);
        var fetched = Fetched(r => r.Year = null);

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_LanguageDisplayNamesFoldToSameCode()
    {
        var book = Book(language: "en");
        var fetched = Fetched(r => r.Language = "English");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_LanguageTrulyChanged_ProducesLanguageDiff()
    {
        var book = Book(language: "en");
        var fetched = Fetched(r => r.Language = "Dansk");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Language", diffs[0].Field);
        Assert.AreEqual("en", diffs[0].LibraryValue);
        Assert.AreEqual("da", diffs[0].SourceValue);
    }

    [TestMethod]
    public void Diff_GenresCompareAsSets()
    {
        var book = Book();
        book.Genres = new List<Genre> { new Genre(1, "Fantasy"), new Genre(2, "Adventure") };
        var fetched = Fetched(r => r.Genres = new List<string> { "Adventure", "Fantasy" });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_UnrecognizedSourceLanguage_IsNotOffered()
    {
        var book = Book(language: "en");
        var fetched = Fetched(r => r.Language = "Klingonese");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_UnrecognizedStoredAndSourceLanguage_NoSpuriousDiff()
    {
        // Regression: a book whose language was backfilled straight from an m4b tag ("spa") is
        // not in the managed alias table; if the source is equally unrecognized, both sides
        // normalize to null and the source fallback must land on the stored value - a symmetric
        // fallback used to be missing there, reporting "spa → (blank)" for a nothing-changed pair.
        var book = Book(language: "spa");
        var fetched = Fetched(r => r.Language = "spa");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_UnrecognizedStoredLanguage_RecognizedSource_OffersTheManagedCode()
    {
        var book = Book(language: "spa");
        var fetched = Fetched(r => r.Language = "English");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Language", diffs[0].Field);
        Assert.AreEqual("spa", diffs[0].LibraryValue);
        Assert.AreEqual("en", diffs[0].SourceValue);
    }
}