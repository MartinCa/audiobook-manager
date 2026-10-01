using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using ScrapingPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The book's recorded "title is split at the colon" choice: a split made while applying metadata
/// is remembered on the book, and a later refresh (diff and apply) then splits the source's
/// "Title: Subtitle" on its own instead of proposing to undo it.
/// </summary>
[TestClass]
public class SplitTitleOnColonFlagTests
{
    private static Domain.Audiobook DomainBook(bool split = false, string name = "The Hobbit", string? subtitle = null) =>
        new(new List<ScrapingPerson>(), name, 2010, new AudiobookFileInfo("/library/b.m4b", "b.m4b", 1))
        {
            Subtitle = subtitle,
            SplitTitleOnColon = split,
        };

    private static PendingRefreshPayload.Snapshot Snapshot(string bookName, string? subtitle = null) =>
        new(PendingRefreshPayload.CurrentVersion, "https://x/y", "Goodreads",
            new List<string>(), new List<string>(), bookName, subtitle, null, null, null,
            new List<string>(), null, null, null, null, null, null);

    private static HashSet<string> Fields(params string[] fields) => new(fields);

    private static Database.Models.Audiobook DbBook(bool split, string name, string? subtitle) =>
        new(1, name, subtitle, null, null, 2010, null, null, null, "en", null, null, null, null, null, "/library/b.m4b", "b.m4b", 1)
        {
            SplitTitleOnColon = split,
        };

    // ---- applier ----

    [TestMethod]
    public void ApplyFields_ExplicitSplitThatSplits_RecordsTheFlagOnTheBook()
    {
        var book = DomainBook();

        MetadataRefreshApplier.ApplyFields(
            book, Snapshot("The Hobbit: There and Back Again"),
            Fields(MetadataRefreshFields.BookName), splitTitleOnColon: true);

        Assert.AreEqual("The Hobbit", book.BookName);
        Assert.AreEqual("There and Back Again", book.Subtitle);
        Assert.IsTrue(book.SplitTitleOnColon);
    }

    [TestMethod]
    public void ApplyFields_ExplicitSplitOnTitleWithoutColon_DoesNotFlagTheBook()
    {
        var book = DomainBook();

        MetadataRefreshApplier.ApplyFields(
            book, Snapshot("The Shining"), Fields(MetadataRefreshFields.BookName), splitTitleOnColon: true);

        Assert.IsFalse(book.SplitTitleOnColon);
    }

    [TestMethod]
    public void ApplyFields_SplitNotRequested_LeavesTheFlagAlone()
    {
        var book = DomainBook();

        MetadataRefreshApplier.ApplyFields(
            book, Snapshot("The Hobbit: There and Back Again"), Fields(MetadataRefreshFields.BookName));

        Assert.AreEqual("The Hobbit: There and Back Again", book.BookName);
        Assert.IsFalse(book.SplitTitleOnColon);
    }

    [TestMethod]
    public void ApplyFields_BookNameNotSelected_DoesNotFlagTheBook()
    {
        var book = DomainBook();

        MetadataRefreshApplier.ApplyFields(
            book, Snapshot("The Hobbit: There and Back Again"),
            Fields(MetadataRefreshFields.Description), splitTitleOnColon: true);

        Assert.IsFalse(book.SplitTitleOnColon);
    }

    [TestMethod]
    public void ApplyFields_FlaggedBook_SplitsWithoutBeingAskedAndKeepsTheFlag()
    {
        var book = DomainBook(split: true);

        MetadataRefreshApplier.ApplyFields(
            book, Snapshot("The Hobbit: There and Back Again"),
            Fields(MetadataRefreshFields.BookName), splitTitleOnColon: false);

        Assert.AreEqual("The Hobbit", book.BookName);
        Assert.AreEqual("There and Back Again", book.Subtitle);
        Assert.IsTrue(book.SplitTitleOnColon);
    }

    [TestMethod]
    public void ApplyFields_FlaggedBook_OnTitleWithoutColonKeepsTitleWholeAndKeepsTheFlag()
    {
        var book = DomainBook(split: true, name: "Old");

        MetadataRefreshApplier.ApplyFields(
            book, Snapshot("4:50 from Paddington"), Fields(MetadataRefreshFields.BookName));

        Assert.AreEqual("4:50 from Paddington", book.BookName);
        Assert.IsNull(book.Subtitle);
        Assert.IsTrue(book.SplitTitleOnColon);
    }

    // ---- differ ----

    [TestMethod]
    public void Diff_FlaggedBookAlreadySplit_ProposesNoTitleOrSubtitleChange()
    {
        var book = DbBook(split: true, "The Hobbit", "There and Back Again");
        var fetched = new MetadataSearchResult("https://x/y", "The Hobbit: There and Back Again")
        {
            Source = "Goodreads",
            Authors = new List<ScrapingPerson>(),
            Narrators = new List<ScrapingPerson>(),
            Genres = new List<string>(),
            Year = 2010,
            Language = "en",
        };

        var diffs = MetadataRefreshDiffer.Diff(book, fetched, InitialsSpacing.Spaced, InitialsPunctuation.Dotted)
            .Where(d => d.Field is MetadataRefreshFields.BookName or MetadataRefreshFields.Subtitle).ToList();

        CollectionAssert.AreEqual(new List<MetadataRefreshDiff>(), diffs);
    }

    [TestMethod]
    public void Diff_UnflaggedBookAlreadySplit_StillProposesUndoingTheSplit()
    {
        var book = DbBook(split: false, "The Hobbit", "There and Back Again");
        var fetched = new MetadataSearchResult("https://x/y", "The Hobbit: There and Back Again")
        {
            Source = "Goodreads",
            Authors = new List<ScrapingPerson>(),
            Narrators = new List<ScrapingPerson>(),
            Genres = new List<string>(),
        };

        var fields = MetadataRefreshDiffer.Diff(book, fetched, InitialsSpacing.Spaced, InitialsPunctuation.Dotted)
            .Select(d => d.Field).ToList();

        CollectionAssert.Contains(fields, MetadataRefreshFields.BookName);
        CollectionAssert.Contains(fields, MetadataRefreshFields.Subtitle);
    }

    [TestMethod]
    public void DiffSnapshot_FlaggedBookAlreadySplit_ProposesNoTitleOrSubtitleChange()
    {
        var book = DbBook(split: true, "The Hobbit", "There and Back Again");

        var fields = MetadataRefreshDiffer.DiffSnapshot(
                book, Snapshot("The Hobbit: There and Back Again"), InitialsSpacing.Spaced, InitialsPunctuation.Dotted)
            .Select(d => d.Field).ToList();

        CollectionAssert.DoesNotContain(fields, MetadataRefreshFields.BookName);
        CollectionAssert.DoesNotContain(fields, MetadataRefreshFields.Subtitle);
    }

    // ---- round trip ----

    [TestMethod]
    public void FromDb_CarriesTheFlag_SoRewritesDoNotDropIt()
    {
        var db = DbBook(split: true, "The Hobbit", "There and Back Again");
        db.Authors = new List<Database.Models.Person>();
        db.Narrators = new List<Database.Models.Person>();
        db.Genres = new List<Database.Models.Genre>();

        Assert.IsTrue(AudiobookService.FromDb(db).SplitTitleOnColon);
    }
}
