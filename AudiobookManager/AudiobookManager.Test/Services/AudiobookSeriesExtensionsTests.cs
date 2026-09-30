using AudiobookManager.Domain;

namespace AudiobookManager.Test.Services;

[TestClass]
public class AudiobookSeriesExtensionsTests
{
    private static Audiobook Book(string? series, string? part, params SeriesRelation[] additional) =>
        new(new List<Person>(), "Book", 2024, new AudiobookFileInfo("/x.m4b", "x.m4b", 1))
        {
            Series = series,
            SeriesPart = part,
            AdditionalSeries = additional.ToList(),
        };

    [TestMethod]
    public void SetSeries_OnABookWithNoPrimary_MakesThatSeriesPrimary()
    {
        var book = Book(null, null);

        book.SetSeries("Main", "2");

        Assert.AreEqual("Main", book.Series);
        Assert.AreEqual("2", book.SeriesPart);
        Assert.AreEqual(0, book.AdditionalSeries!.Count);
    }

    [TestMethod]
    public void SetSeries_OnTheExistingPrimary_OnlyChangesItsPart()
    {
        var book = Book("Main", "1", new SeriesRelation("Spinoff", "3"));

        book.SetSeries("main", "9");

        Assert.AreEqual("9", book.SeriesPart);
        Assert.AreEqual("Main", book.Series, "the stored spelling is kept");
        Assert.AreEqual(new SeriesRelation("Spinoff", "3"), book.AdditionalSeries!.Single());
    }

    [TestMethod]
    public void SetSeries_OnAnAdditionalSeries_UpdatesItsPartInPlace()
    {
        var book = Book("Main", "1", new SeriesRelation("Spinoff", "3"));

        book.SetSeries("Spinoff", "4");

        Assert.AreEqual(new SeriesRelation("Spinoff", "4"), book.AdditionalSeries!.Single());
        Assert.AreEqual("1", book.SeriesPart);
    }

    [TestMethod]
    public void RemoveSeries_TheAdditional_LeavesThePrimaryAlone()
    {
        var book = Book("Main", "1", new SeriesRelation("Spinoff", "3"));

        Assert.IsTrue(book.RemoveSeries("Spinoff"));

        Assert.AreEqual("Main", book.Series);
        Assert.AreEqual(0, book.AdditionalSeries!.Count);
    }

    [TestMethod]
    public void RemoveSeries_ThePrimary_PromotesTheFirstAdditionalWithItsPart()
    {
        var book = Book("Main", "1", new SeriesRelation("Spinoff", "3"), new SeriesRelation("Third", "5"));

        book.RemoveSeries("Main");

        Assert.AreEqual("Spinoff", book.Series);
        Assert.AreEqual("3", book.SeriesPart);
        Assert.AreEqual(new SeriesRelation("Third", "5"), book.AdditionalSeries!.Single());
    }

    [TestMethod]
    public void RemoveSeries_TheOnlySeries_LeavesTheBookWithNone()
    {
        var book = Book("Main", "1");

        book.RemoveSeries("Main");

        Assert.AreEqual(string.Empty, book.Series);
        Assert.IsNull(book.SeriesPart);
    }

    [TestMethod]
    public void RemoveSeries_ASeriesTheBookIsNotIn_ReportsFalse()
    {
        Assert.IsFalse(Book("Main", "1").RemoveSeries("Nope"));
    }

    [TestMethod]
    public void RenameSeries_ThePrimary_KeepsItPrimaryWithItsPart()
    {
        var book = Book("Old", "2", new SeriesRelation("Spinoff", "3"));

        book.RenameSeries("Old", "New");

        Assert.AreEqual("New", book.Series);
        Assert.AreEqual("2", book.SeriesPart);
    }

    [TestMethod]
    public void RenameSeries_AnAdditional_RenamesItInPlace()
    {
        var book = Book("Main", "1", new SeriesRelation("Old", "3"));

        book.RenameSeries("Old", "New");

        Assert.AreEqual(new SeriesRelation("New", "3"), book.AdditionalSeries!.Single());
    }

    [TestMethod]
    public void RenameSeries_OntoARelationTheBookAlreadyHas_MergesIntoOneKeepingThatPart()
    {
        var book = Book("Main", "1", new SeriesRelation("Old", "3"), new SeriesRelation("New", "8"));

        book.RenameSeries("Old", "New");

        CollectionAssert.AreEqual(
            new[] { ("Main", "1", true), ("New", "8", false) },
            book.AllSeries().Select(r => (r.Name, r.Part, r.IsPrimary)).ToArray());
    }

    [TestMethod]
    public void RenameSeries_APrimaryOntoAnExistingAdditional_TheMergedRelationStaysPrimary()
    {
        var book = Book("Old", "2", new SeriesRelation("Other", "5"), new SeriesRelation("New", null));

        book.RenameSeries("Old", "New");

        CollectionAssert.AreEqual(
            new[] { ("New", "2", true), ("Other", "5", false) },
            book.AllSeries().Select(r => (r.Name, r.Part, r.IsPrimary)).ToArray(),
            "the merge takes the old part when the target had none, and keeps the primary slot");
    }

    [TestMethod]
    public void PromoteAdditionalSeriesIfNoPrimary_FilesABookWithOnlyAdditionalSeriesUnderTheFirst()
    {
        var book = Book(null, null, new SeriesRelation("Spinoff", "3"), new SeriesRelation("Third", "5"));

        book.PromoteAdditionalSeriesIfNoPrimary();

        Assert.AreEqual("Spinoff", book.Series);
        Assert.AreEqual("3", book.SeriesPart);
        Assert.AreEqual(new SeriesRelation("Third", "5"), book.AdditionalSeries!.Single());
    }

    [TestMethod]
    public void PromoteAdditionalSeriesIfNoPrimary_LeavesABookThatHasAPrimaryAlone()
    {
        var book = Book("Main", "1", new SeriesRelation("Spinoff", "3"));

        book.PromoteAdditionalSeriesIfNoPrimary();

        Assert.AreEqual("Main", book.Series);
        Assert.AreEqual(new SeriesRelation("Spinoff", "3"), book.AdditionalSeries!.Single());
    }

    [TestMethod]
    public void PromoteAdditionalSeriesIfNoPrimary_IsANoOpWithoutAdditionalSeriesOrWhenUnspecified()
    {
        var none = Book(null, null);
        none.PromoteAdditionalSeriesIfNoPrimary();
        Assert.IsNull(none.Series);

        var unspecified = Book(null, null);
        unspecified.AdditionalSeries = null;
        unspecified.PromoteAdditionalSeriesIfNoPrimary();
        Assert.IsNull(unspecified.Series);
    }
}
