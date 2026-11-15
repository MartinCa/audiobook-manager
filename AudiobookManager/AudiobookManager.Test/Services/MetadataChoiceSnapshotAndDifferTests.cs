using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DbPerson = AudiobookManager.Database.Models.Person;
using DomainPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The source's main author / main series designation as it is stored in the pending snapshot, and the
/// diff the choice rules produce from it.
/// </summary>
[TestClass]
public class MetadataChoiceSnapshotAndDifferTests
{
    private static MetadataSearchResult Fetched(
        string[] authors, string? mainAuthor = null, (string Name, string? Part)[]? series = null, string? mainSeries = null) =>
        new("https://www.audible.com/pd/x", "A Book")
        {
            Source = "Audible",
            Year = 2024,
            Authors = authors.Select(a => new DomainPerson(a)).ToList(),
            MainAuthor = mainAuthor,
            Narrators = new List<DomainPerson>(),
            Genres = new List<string>(),
            Series = series?.Select(s => new MetadataSeriesSearchResult(s.Name) { SeriesPart = s.Part }).ToList(),
            MainSeries = mainSeries,
        };

    private static DbAudiobook Book(string[] authors, string? main, params string[] series)
    {
        var book = new DbAudiobook(
            1, "A Book", null, series.FirstOrDefault(), null, 2024, null, null, null, null, null, null, null, null, null,
            "/library/b.m4b", "b.m4b", 1);
        for (var i = 1; i < series.Length; i++)
        {
            book.SeriesRelations!.Add(new AudiobookSeries { SeriesName = series[i], IsPrimary = false, SortOrder = i });
        }

        // Everything the tests are not about matches the source, so only authors/series can differ.
        book.Language = "en";
        book.Www = "https://www.audible.com/pd/x";
        book.Authors = authors.Select((n, i) => new DbPerson(i + 1, n)).ToList();
        book.MainAuthorId = book.Authors.FirstOrDefault(a => a.Name == main)?.Id;
        return book;
    }

    private static MetadataChoiceRuleSet Rules(string kind, SourceNamesMainRule named, SourceSilentRule silent = SourceSilentRule.KeepCurrent) =>
        MetadataChoiceRuleSet.From(new Dictionary<string, ChoiceRule> { [kind] = new(named, silent) });

    private static List<MetadataRefreshDiff> Diff(DbAudiobook book, MetadataSearchResult fetched, MetadataChoiceRuleSet? rules = null) =>
        MetadataRefreshDiffer.Diff(book, fetched, Domain.InitialsSpacing.Unspaced, Domain.InitialsPunctuation.Dotted, choiceRules: rules).ToList();

    #region Snapshot

    [TestMethod]
    public void Snapshot_StoresTheSourcesMainAuthor_AndTheMainSeriesOnItsEntry()
    {
        var fetched = Fetched(["A One", "B Two"], "B Two", [("Saga", "1"), ("Cosmere", "3")], "Cosmere");

        var snapshot = PendingRefreshPayload.TryParse(PendingRefreshPayload.Serialize(PendingRefreshPayload.FromSearchResult(fetched)))!;

        Assert.AreEqual(PendingRefreshPayload.CurrentVersion, snapshot.Version);
        Assert.AreEqual("B Two", snapshot.MainAuthor);
        Assert.AreEqual("Cosmere", PendingRefreshPayload.MainSeriesOf(snapshot));
        Assert.IsNull(snapshot.Series!.Single(s => s.Name == "Saga").IsMain);
    }

    [TestMethod]
    public void Snapshot_ASourceThatDoesNotSay_StoresNoDesignation_AndTheJsonOmitsIt()
    {
        var snapshot = PendingRefreshPayload.FromSearchResult(
            Fetched(["A One", "B Two"], series: [("Saga", "1"), ("Cosmere", "3")]));

        var json = PendingRefreshPayload.Serialize(snapshot);

        Assert.IsNull(snapshot.MainAuthor);
        Assert.IsNull(PendingRefreshPayload.MainSeriesOf(snapshot));
        Assert.IsFalse(json.Contains("mainAuthor"), json);
        Assert.IsFalse(json.Contains("isMain"), json);
    }

    [TestMethod]
    public void Snapshot_AMainSeriesNotAmongTheSeries_IsNotStoredAsOne()
    {
        var snapshot = PendingRefreshPayload.FromSearchResult(
            Fetched(["A One"], series: [("Saga", "1"), ("Cosmere", "3")], mainSeries: "Elsewhere"));

        Assert.IsNull(PendingRefreshPayload.MainSeriesOf(snapshot));
    }

    [TestMethod]
    public void Snapshot_ARowWrittenBeforeVersion7_ReadsAsTheSourceNotSayingAnything()
    {
        var legacy = """{"version":6,"url":"https://x","source":"Audible","authors":["A One"],"narrators":[],"bookName":"B","genres":[],"series":[{"name":"S","part":"1"}]}""";

        var snapshot = PendingRefreshPayload.TryParse(legacy)!;

        Assert.IsNull(snapshot.MainAuthor);
        Assert.IsNull(PendingRefreshPayload.MainSeriesOf(snapshot));
        // And re-serializing it adds nothing, so a re-evaluation does not rewrite every legacy row.
        Assert.IsFalse(PendingRefreshPayload.Serialize(snapshot).Contains("isMain"));
    }

    [TestMethod]
    public void Snapshot_TheMainSeriesDesignationSurvivesReplacingTheSeries()
    {
        var snapshot = PendingRefreshPayload.FromSearchResult(
            Fetched(["A One"], series: [("Saga", "1"), ("Cosmere", "3")], mainSeries: "Cosmere"));
        var entries = snapshot.Series!;

        var replaced = PendingRefreshPayload.WithSeries(snapshot, entries);

        Assert.AreEqual("Cosmere", PendingRefreshPayload.MainSeriesOf(replaced));
    }

    #endregion

    #region Differ

    [TestMethod]
    public void Diff_WithDefaultRules_TheSourcesDesignationChangesNothing()
    {
        var diffs = Diff(
            Book(["A One", "B Two"], null, "Saga"),
            Fetched(["A One", "B Two"], "A One", [("Saga", "1"), ("Cosmere", "3")], "Cosmere"));

        // The only difference is the source's second series; neither designation is acted on.
        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual(MetadataRefreshFields.Series, diffs[0].Field);
    }

    [TestMethod]
    public void Diff_SameAuthors_ButTheRulesWouldSetADifferentMainAuthor_OffersAnAuthorsChangeSpelledOut()
    {
        var diffs = Diff(
            Book(["A One", "B Two"], "B Two"),
            Fetched(["A One", "B Two"], "A One"),
            Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources));

        var diff = diffs.Single();
        Assert.AreEqual(MetadataRefreshFields.Authors, diff.Field);
        Assert.AreEqual("A One / B Two (main: B Two)", diff.LibraryValue);
        Assert.AreEqual("A One / B Two (main: A One)", diff.SourceValue);
    }

    [TestMethod]
    public void Diff_SameAuthors_RulesThatWouldClearTheMainAuthor_OfferThatToo()
    {
        var diffs = Diff(
            Book(["A One", "B Two"], "B Two"),
            Fetched(["A One", "B Two"]),
            Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.KeepCurrent, SourceSilentRule.None));

        var diff = diffs.Single();
        StringAssert.Contains(diff.LibraryValue!, "main: B Two");
        StringAssert.Contains(diff.SourceValue!, "no main author");
    }

    [TestMethod]
    public void Diff_SameAuthorsAndTheSameMainAuthor_IsNoDifference()
    {
        var diffs = Diff(
            Book(["A One", "B Two"], "A One"),
            Fetched(["A One", "B Two"], "A One"),
            Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources));

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_AskMeNeverCreatesADifferenceOnItsOwn()
    {
        // Ask me pre-selects the book's own, so with the authors unchanged there is nothing to offer.
        var diffs = Diff(
            Book(["A One", "B Two"], null),
            Fetched(["A One", "B Two"], "A One"),
            Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.AskMe, SourceSilentRule.AskMe));

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void Diff_AuthorsThatDiffer_KeepTheirPlainValues_NoMainAuthorMarker()
    {
        var diffs = Diff(
            Book(["A One"], null),
            Fetched(["A One", "B Two"], "B Two"),
            Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources));

        var diff = diffs.Single();
        Assert.AreEqual("A One", diff.LibraryValue);
        Assert.AreEqual("A One / B Two", diff.SourceValue);
    }

    [TestMethod]
    public void Diff_APrimarySeriesTheRulesWouldChange_IsASeriesDifference()
    {
        var diffs = Diff(
            Book(["A One"], null, "Saga", "Cosmere"),
            Fetched(["A One"], series: [("Saga", "1"), ("Cosmere", "3")], mainSeries: "Cosmere"),
            Rules(MetadataChoiceKinds.PrimarySeries, SourceNamesMainRule.UseSources));

        var diff = diffs.Single();
        Assert.AreEqual(MetadataRefreshFields.Series, diff.Field);
        StringAssert.Contains(diff.SourceValue!, "Cosmere");
    }

    [TestMethod]
    public void Diff_APrimarySeriesTheRulesWouldKeep_IsNoDifference()
    {
        var diffs = Diff(
            Book(["A One"], null, "Saga", "Cosmere"),
            Fetched(["A One"], series: [("Saga", null), ("Cosmere", null)], mainSeries: "Cosmere"),
            Rules(MetadataChoiceKinds.PrimarySeries, SourceNamesMainRule.KeepCurrent));

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void DiffSnapshot_AgreesWithDiff_ForTheSameSourceAndRules()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources);
        var book = Book(["A One", "B Two"], "B Two");
        var fetched = Fetched(["A One", "B Two"], "A One");

        var fromFetched = Diff(book, fetched, rules);
        var fromSnapshot = MetadataRefreshDiffer.DiffSnapshot(
            book, PendingRefreshPayload.FromSearchResult(fetched), Domain.InitialsSpacing.Unspaced, Domain.InitialsPunctuation.Dotted, rules).ToList();

        CollectionAssert.AreEqual(
            fromFetched.Select(d => (d.Field, d.LibraryValue, d.SourceValue)).ToList(),
            fromSnapshot.Select(d => (d.Field, d.LibraryValue, d.SourceValue)).ToList());
    }

    #endregion
}
