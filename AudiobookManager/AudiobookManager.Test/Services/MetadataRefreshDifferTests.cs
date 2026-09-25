using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using AudiobookManager.Scraping.Models;
using ScrapingPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataRefreshDifferTests
{
    // Defaults to the same URL Fetched() defaults to, so a test that doesn't care about Www (the
    // vast majority) doesn't spuriously pick up a diff for it - matching how every other Book()
    // default already mirrors Fetched()'s "The Test Book" title.
    private const string DefaultWww = "https://www.audible.com/pd/test";

    private static Database.Models.Audiobook Book(
        string? bookName = null, string? subtitle = null, string? series = null,
        string? seriesPart = null, int year = 2010, string? language = "en",
        string? rating = null, string? description = null, string? www = DefaultWww)
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
            www: www,
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

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_ChangedBookName_ProducesSingleDiff()
    {
        var book = Book();
        var fetched = Fetched(r => r.BookName = "The Test Book (Unabridged)");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

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

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

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

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

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

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_EmptyVsNull_IsNotADifference()
    {
        var book = Book(subtitle: "");
        var fetched = Fetched(r => r.Subtitle = null);

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_SourceLacksYear_DoesNotOfferBlankingYear()
    {
        var book = Book(year: 2010);
        var fetched = Fetched(r => r.Year = null);

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_LanguageDisplayNamesFoldToSameCode()
    {
        var book = Book(language: "en");
        var fetched = Fetched(r => r.Language = "English");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_LanguageTrulyChanged_ProducesLanguageDiff()
    {
        var book = Book(language: "en");
        var fetched = Fetched(r => r.Language = "Dansk");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

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

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_UnrecognizedSourceLanguage_IsNotOffered()
    {
        var book = Book(language: "en");
        var fetched = Fetched(r => r.Language = "Klingonese");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

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

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_AuthorInitialsSpacingOnlyDiffers_ProducesNoDiff()
    {
        // Regression: "Stephen M. R. Covey" (spaced) vs "Stephen M.R. Covey" (unspaced) is the
        // same author under a different initials-spacing convention, not a real content change.
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "Stephen M. R. Covey") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson> { new("Stephen M.R. Covey") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_NarratorInitialsSpacingOnlyDiffers_ProducesNoDiff()
    {
        var book = Book();
        book.Narrators = new List<Database.Models.Person> { new(1, "J.K. Rowling") };

        var fetched = Fetched(r =>
        {
            r.Narrators = new List<ScrapingPerson> { new("J. K. Rowling") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_AuthorNameActuallyDifferent_StillDetectedDespiteInitialsFold()
    {
        // Proves the initials-spacing fold does not swallow real changes.
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "Stephen R. Covey") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson> { new("Stephen M.R. Covey") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Authors", diffs[0].Field);
    }

    // Regression (reported live against a real library, follow-up to the initials-spacing fix
    // above): "Andrew R. Chow" (library, Dotted) vs "Andrew R Chow" (a source that simply omits
    // the period) is a PUNCTUATION difference, not a spacing one - the earlier fix only collapsed
    // the space between two adjacent dotted initials and did nothing for a missing dot, so this
    // exact pair still showed as a spurious Authors change. Both sides must be formatted to the
    // library's configured InitialsPunctuation (not just InitialsSpacing) before comparing.
    [TestMethod]
    public void Diff_AuthorInitialsPunctuationOnlyDiffers_ProducesNoDiffUnderDottedSetting()
    {
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "Andrew R. Chow") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson> { new("Andrew R Chow") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_AuthorInitialsPunctuationOnlyDiffers_ProducesNoDiffUnderUndottedSetting()
    {
        // Same pair, opposite library setting: proves this isn't hardcoded to "prefer dots" - the
        // comparison must follow whatever the library is actually configured for.
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "Andrew R Chow") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson> { new("Andrew R. Chow") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Undotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_AuthorInitialsSpacingAndPunctuationBothDiffer_ProducesNoDiff()
    {
        // The fully general case this fix is meant to cover: the source's convention need not
        // match the library's in EITHER respect at once. Library convention is Spaced+Dotted
        // ("J. R. R. Tolkien"); the stored name happens to be Spaced+Undotted and the fetched
        // name happens to be Unspaced+Dotted - both format to the same canonical value.
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "J R R Tolkien") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson> { new("J.R.R. Tolkien") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_LoneMiddleInitialActuallyDifferent_StillDetected()
    {
        // Proves the lone-initial dot fold doesn't swallow a genuinely different initial - "R"
        // and "S" are never the same letter regardless of punctuation.
        var book = Book();
        book.Authors = new List<Database.Models.Person> { new(1, "Andrew R. Chow") };

        var fetched = Fetched(r =>
        {
            r.Authors = new List<ScrapingPerson> { new("Andrew S Chow") };
        });

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Authors", diffs[0].Field);
    }

    [TestMethod]
    public void Diff_UnrecognizedStoredLanguage_RecognizedSource_OffersTheManagedCode()
    {
        var book = Book(language: "spa");
        var fetched = Fetched(r => r.Language = "English");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Language", diffs[0].Field);
        Assert.AreEqual("spa", diffs[0].LibraryValue);
        Assert.AreEqual("en", diffs[0].SourceValue);
    }

    // Regression tests: a bulk online-match candidate is selected for a book that has never been
    // matched to any source before (Www null), unlike "Refresh Now" which only ever re-fetches
    // from a book's *already-known* source (Www already equal to what's being diffed against, so
    // it never surfaced a diff and the differ never needed to compare it). Without a Www
    // comparison, applying every other field via the bulk MetadataRefresh page's field-limited
    // applier left the book fully re-tagged but still unmatched to the source it was just applied
    // from - no Www, no MatchedSourceName, "matching" silently didn't stick.
    [TestMethod]
    public void Diff_BookHasNoWwwAndSourceHasUrl_OffersWwwDiff()
    {
        var book = Book(www: null);
        var fetched = Fetched();

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Www", diffs[0].Field);
        Assert.IsNull(diffs[0].LibraryValue);
        Assert.AreEqual("https://www.audible.com/pd/test", diffs[0].SourceValue);
    }

    [TestMethod]
    public void Diff_SameUrlDifferentTrackingParams_ProducesNoWwwDiff()
    {
        // The comparison is against fetched.CleanUrl (tracking/session params stripped), not the
        // raw Url - a source re-reporting the same book with different tracking params on the
        // link must not look like a changed match.
        var book = Book(www: "https://www.audible.com/pd/test");
        var fetched = Fetched(r => r.Url = "https://www.audible.com/pd/test?ref=abc123&pf_rd_p=xyz");

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    private static PendingRefreshPayload.Snapshot Snapshot(string? www = "https://www.audible.com/pd/test") =>
        new(
            PendingRefreshPayload.CurrentVersion,
            www ?? "",
            "Audible",
            new List<string>(),
            new List<string>(),
            "The Test Book",
            null,
            null,
            null,
            null,
            new List<string>(),
            null,
            null,
            null,
            null,
            null,
            null);

    [TestMethod]
    public void DiffSnapshot_BookHasNoWwwAndSnapshotHasUrl_OffersWwwDiff()
    {
        var book = Book(www: null);
        var snapshot = Snapshot();

        var diffs = MetadataRefreshDiffer.DiffSnapshot(book, snapshot, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Www", diffs[0].Field);
        Assert.IsNull(diffs[0].LibraryValue);
        Assert.AreEqual("https://www.audible.com/pd/test", diffs[0].SourceValue);
    }

    [TestMethod]
    public void DiffSnapshot_SameWww_ProducesNoDiff()
    {
        var book = Book(www: "https://www.audible.com/pd/test");
        var snapshot = Snapshot();

        var diffs = MetadataRefreshDiffer.DiffSnapshot(book, snapshot, Domain.InitialsSpacing.Spaced, Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }
}