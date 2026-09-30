using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using ScrapingPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

/// <summary>
/// A refresh now proposes a book's whole set of series, not the first one a source lists. What
/// matters: the source's ordering never produces a diff, each real change does, the same choice of
/// primary is made when diffing and when applying, and stored rows from before the change still read.
/// </summary>
[TestClass]
public class MetadataRefreshSeriesTests
{
    private static Database.Models.Audiobook Book(string? series, string? part, params (string Name, string? Part)[] additional)
    {
        var book = new Database.Models.Audiobook(
            1, "The Test Book", null, series, part, 2010, null, null, null, "en", null, null,
            "https://www.audible.com/pd/test", null, null, "/library/book.m4b", "book.m4b", 123)
        {
            SeriesRelations = new List<AudiobookSeries>(),
        };
        if (!string.IsNullOrEmpty(series))
        {
            book.SeriesRelations.Add(new AudiobookSeries { SeriesName = series, SeriesPart = part, IsPrimary = true, SortOrder = 0 });
        }

        var order = 1;
        foreach (var (name, p) in additional)
        {
            book.SeriesRelations.Add(new AudiobookSeries { SeriesName = name, SeriesPart = p, SortOrder = order++ });
        }

        return book;
    }

    private static MetadataSearchResult Fetched(params (string Name, string? Part)[] series) =>
        new("https://www.audible.com/pd/test", "The Test Book")
        {
            Source = "Audible",
            Authors = new List<ScrapingPerson>(),
            Narrators = new List<ScrapingPerson>(),
            Genres = new List<string>(),
            Series = series.Select(s => new MetadataSeriesSearchResult(s.Name) { SeriesPart = s.Part }).ToList(),
        };

    private static List<MetadataRefreshDiff> Diff(Database.Models.Audiobook book, MetadataSearchResult fetched) =>
        MetadataRefreshDiffer.Diff(book, fetched, AudiobookManager.Domain.InitialsSpacing.Spaced, AudiobookManager.Domain.InitialsPunctuation.Dotted).ToList();

    [TestMethod]
    public void Diff_SameSeriesInADifferentSourceOrder_ProducesNoDifference()
    {
        var book = Book("Main", "1", ("Spinoff", "3"));

        Assert.AreEqual(0, Diff(book, Fetched(("Spinoff", "3"), ("Main", "1"))).Count);
        Assert.AreEqual(0, Diff(book, Fetched(("Main", "1"), ("Spinoff", "3"))).Count);
    }

    [TestMethod]
    public void Diff_TheSourceListingTheBooksPrimarySecond_DoesNotChangeThePrimary()
    {
        // The property the old "take [0]" lacked: a source that lists Spinoff first must not
        // re-file a book whose primary Main is still listed.
        var book = Book("Main", "1", ("Spinoff", "3"));

        var fetched = Fetched(("Spinoff", "3"), ("Main", "1"));

        Assert.AreEqual(0, Diff(book, fetched).Count);
    }

    [TestMethod]
    public void Diff_AnAddedSeries_IsOneSeriesDiffShowingBothSets()
    {
        var book = Book("Main", "1");

        var diffs = Diff(book, Fetched(("Main", "1"), ("Spinoff", "3")));

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("Series", diffs[0].Field);
        Assert.AreEqual("Main #1", diffs[0].LibraryValue);
        Assert.AreEqual("Main #1 (primary); Spinoff #3", diffs[0].SourceValue);
    }

    [TestMethod]
    public void Diff_ARemovedSeries_IsADiff()
    {
        var book = Book("Main", "1", ("Spinoff", "3"));

        var diffs = Diff(book, Fetched(("Main", "1")));

        Assert.AreEqual("Series", diffs.Single().Field);
    }

    [TestMethod]
    public void Diff_ADifferentPartInOneSeries_IsADiff()
    {
        var book = Book("Main", "1", ("Spinoff", "3"));

        Assert.AreEqual("Series", Diff(book, Fetched(("Main", "1"), ("Spinoff", "4"))).Single().Field);
        Assert.AreEqual("Series", Diff(book, Fetched(("Main", "2"), ("Spinoff", "3"))).Single().Field);
    }

    [TestMethod]
    public void Diff_ACosmeticallyDifferentPart_IsNotADiff()
    {
        var book = Book("Main", "1", ("Spinoff", "3"));

        Assert.AreEqual(0, Diff(book, Fetched(("Main", "1.0"), ("Spinoff", " 3 "))).Count);
    }

    [TestMethod]
    public void Diff_ADifferentSeriesName_IsADiffEvenWhenOnlyTheCasingDiffers()
    {
        var book = Book("main", "1");

        Assert.AreEqual("Series", Diff(book, Fetched(("Main", "1"))).Single().Field);
    }

    [TestMethod]
    public void Diff_ThePrimaryGoneFromTheSource_ProposesTheSourcesFirstAsPrimary()
    {
        var book = Book("Old", "1", ("Spinoff", "3"));

        var diffs = Diff(book, Fetched(("Other", "2"), ("Spinoff", "3")));

        // Spinoff is a series the book already has, so it is preferred over the source's first.
        Assert.AreEqual("Series", diffs.Single().Field);
        Assert.AreEqual("Spinoff #3 (primary); Other #2", diffs[0].SourceValue);
    }

    [TestMethod]
    public void Diff_NeverReportsSeriesPartAsAFieldOfItsOwn()
    {
        var book = Book("Main", "1");

        var diffs = Diff(book, Fetched(("Main", "2")));

        CollectionAssert.AreEqual(new[] { "Series" }, diffs.Select(d => d.Field).ToArray());
    }

    [TestMethod]
    public void DiffSnapshot_AgreesWithDiffForTheSameSource()
    {
        var book = Book("Main", "1", ("Spinoff", "3"));
        var fetched = Fetched(("Spinoff", "4"), ("Main", "1"));

        var fromFetched = Diff(book, fetched).Select(d => (d.Field, d.LibraryValue, d.SourceValue)).ToArray();
        var fromSnapshot = MetadataRefreshDiffer
            .DiffSnapshot(book, PendingRefreshPayload.FromSearchResult(fetched), AudiobookManager.Domain.InitialsSpacing.Spaced, AudiobookManager.Domain.InitialsPunctuation.Dotted)
            .Select(d => (d.Field, d.LibraryValue, d.SourceValue)).ToArray();

        CollectionAssert.AreEqual(fromFetched, fromSnapshot);
    }

    [TestMethod]
    public void DiffSnapshot_ALegacySingleSeriesSnapshot_IsReadAsThatOneSeries()
    {
        var book = Book("Main", "1");
        var legacy = PendingRefreshPayload.FromSearchResult(Fetched()) with
        {
            Version = 3, SeriesName = "Main", SeriesPart = "1", Series = null,
        };

        var diffs = MetadataRefreshDiffer.DiffSnapshot(book, legacy, AudiobookManager.Domain.InitialsSpacing.Spaced, AudiobookManager.Domain.InitialsPunctuation.Dotted).ToList();

        Assert.AreEqual(0, diffs.Count);
    }

    // --- the primary choice, made the same way when applying -------------------------------

    private static AudiobookManager.Domain.Audiobook DomainBook(string? series, string? part, params SeriesRelation[] additional) =>
        new(new List<ScrapingPerson> { new("A") }, "The Test Book", 2010, new AudiobookFileInfo("/x.m4b", "x.m4b", 1))
        {
            Series = series,
            SeriesPart = part,
            AdditionalSeries = additional.ToList(),
        };

    [TestMethod]
    public void ApplyFields_Series_ReplacesTheWholeSetAndKeepsTheCurrentPrimaryWhenTheSourceStillHasIt()
    {
        var book = DomainBook("Main", "1", new SeriesRelation("Old Spinoff", "9"));
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched(("Spinoff", "3"), ("Main", "2")));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { "Series" });

        Assert.AreEqual("Main", book.Series);
        Assert.AreEqual("2", book.SeriesPart);
        CollectionAssert.AreEqual(new[] { new SeriesRelation("Spinoff", "3") }, book.AdditionalSeries!.ToArray());
    }

    [TestMethod]
    public void ApplyFields_AnExplicitPrimaryWins()
    {
        var book = DomainBook("Main", "1");
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched(("Main", "1"), ("Spinoff", "3")));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { "Series" }, primarySeriesName: "Spinoff");

        Assert.AreEqual("Spinoff", book.Series);
        Assert.AreEqual("3", book.SeriesPart);
        CollectionAssert.AreEqual(new[] { new SeriesRelation("Main", "1") }, book.AdditionalSeries!.ToArray());
    }

    [TestMethod]
    public void ApplyFields_AnExplicitPrimaryTheSourceDoesNotHave_IsIgnored()
    {
        var book = DomainBook("Main", "1");
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched(("Main", "1"), ("Spinoff", "3")));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { "Series" }, primarySeriesName: "Nope");

        Assert.AreEqual("Main", book.Series);
    }

    [TestMethod]
    public void ApplyFields_ABookWithNoSeries_TakesTheSourcesFirstAsPrimary()
    {
        var book = DomainBook(null, null);
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched(("Spinoff", "3"), ("Main", "1")));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { "Series" });

        // Canonical order is by name, so the choice does not depend on how the source ordered them.
        Assert.AreEqual("Main", book.Series);
        Assert.AreEqual("1", book.SeriesPart);
        CollectionAssert.AreEqual(new[] { new SeriesRelation("Spinoff", "3") }, book.AdditionalSeries!.ToArray());
    }

    [TestMethod]
    public void ApplyFields_TheRetiredSeriesPartField_AppliesTheWholeSeriesSetNotAPartOnItsOwn()
    {
        var book = DomainBook("Main", "1");
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched(("Other", "5")));

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { "SeriesPart" });

        Assert.AreEqual("Other", book.Series, "a part must never be applied without its series");
        Assert.AreEqual("5", book.SeriesPart);
    }

    [TestMethod]
    public void ApplyFields_TheSourceReportingNoSeries_ClearsThemWhenSeriesIsSelected()
    {
        var book = DomainBook("Main", "1", new SeriesRelation("Spinoff", "3"));
        var snapshot = PendingRefreshPayload.FromSearchResult(Fetched());

        MetadataRefreshApplier.ApplyFields(book, snapshot, new HashSet<string> { "Series" });

        Assert.IsNull(book.Series);
        Assert.AreEqual(0, book.AdditionalSeries!.Count);
    }

    // --- stored payloads ---------------------------------------------------------------------

    [TestMethod]
    public void FromSearchResult_KeepsEverySeriesWithItsOriginalName()
    {
        var fetched = Fetched(("Mapped", "1"), ("Second", null));
        fetched.Series![0].OriginalSeriesName = "Raw Name";

        var snapshot = PendingRefreshPayload.FromSearchResult(fetched);

        Assert.AreEqual(PendingRefreshPayload.CurrentVersion, snapshot.Version);
        CollectionAssert.AreEqual(
            new[]
            {
                new PendingRefreshPayload.SnapshotSeries("Mapped", "1", "Raw Name"),
                new PendingRefreshPayload.SnapshotSeries("Second", null, "Second"),
            },
            snapshot.Series!.ToArray());
        Assert.AreEqual("Mapped", snapshot.SeriesName, "the first-entry mirror stays for older readers");
    }

    [TestMethod]
    public void TryParse_ALegacyRowWithoutTheSeriesList_ReSerializesWithoutAddingOne()
    {
        // ReevaluatePendingRefreshesAsync compares re-serialized bytes; a legacy row must not look
        // "updated" just because the new property exists.
        var legacy = PendingRefreshPayload.Serialize(PendingRefreshPayload.FromSearchResult(Fetched(("Main", "1"))) with
        {
            Version = 3, Series = null,
        });

        var reserialized = PendingRefreshPayload.Serialize(PendingRefreshPayload.TryParse(legacy)!);

        Assert.AreEqual(legacy, reserialized);
        Assert.IsFalse(legacy.Contains("\"series\":["));
    }

    [TestMethod]
    public void ParseChangedFieldsJson_AStoredSeriesPart_IsReadAsSeries()
    {
        var fields = MetadataRefreshFields.ParseChangedFieldsJson("[\"Series\",\"SeriesPart\",\"Year\"]");

        CollectionAssert.AreEqual(new[] { "Series", "Year" }, fields);
    }

    // --- the shared set logic ----------------------------------------------------------------

    [TestMethod]
    public void Canonicalize_TrimsDropsBlanksMergesDuplicatesAndOrdersByName()
    {
        var result = SeriesRelationSet.Canonicalize(new SeriesRelationSet.Entry[]
        {
            new(" Zed ", null), new("", "1"), new("alpha", "2"), new("ALPHA", "9"), new("zed", "4"),
        });

        CollectionAssert.AreEqual(
            new[] { new SeriesRelationSet.Entry("alpha", "2"), new SeriesRelationSet.Entry("Zed", "4") },
            result.ToArray());
    }

    [TestMethod]
    public void ChoosePrimary_PrefersExplicitThenCurrentThenAnyCurrentRelationThenFirst()
    {
        var source = SeriesRelationSet.Canonicalize(new SeriesRelationSet.Entry[] { new("A", null), new("B", null), new("C", null) });
        var current = new List<SeriesRelationSet.Entry> { new("X", null), new("C", null), new("B", null) };

        Assert.AreEqual("A", SeriesRelationSet.ChoosePrimary(current, source, "A")!.Name);
        Assert.AreEqual("C", SeriesRelationSet.ChoosePrimary(current, source)!.Name, "the first of the book's own series the source lists");
        Assert.AreEqual("A", SeriesRelationSet.ChoosePrimary(new List<SeriesRelationSet.Entry>(), source)!.Name);
        Assert.IsNull(SeriesRelationSet.ChoosePrimary(current, new List<SeriesRelationSet.Entry>()));
    }
}
