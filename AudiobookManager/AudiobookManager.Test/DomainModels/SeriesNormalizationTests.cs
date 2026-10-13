using AudiobookManager.Database.Search;
using AudiobookManager.Database.Sort;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.DomainModels;

[TestClass]
public class SeriesNormalizationTests
{
    [TestMethod]
    [DataRow("2", "2")]
    [DataRow(" 2 ", "2")]
    [DataRow("02", "2")]
    [DataRow("2.0", "2")]
    [DataRow("02.50", "2.5")]
    [DataRow("0.5", "0.5")]
    [DataRow("0", "0")]
    [DataRow("Book 2", "2")]
    [DataRow("book2", "2")]
    [DataRow("Vol. 03", "3")]
    [DataRow("Volume 4.0", "4")]
    [DataRow("1-3", "1-3")]
    [DataRow("01-03", "1-3")]
    [DataRow("1 – 3", "1-3")]
    [DataRow("1—3", "1-3")]
    [DataRow("1 to 3", "1-3")]
    [DataRow("Book 1-3", "1-3")]
    [DataRow("1.5-2.0", "1.5-2")]
    [DataRow("Prequel", "Prequel")]
    [DataRow("  Prequel ", "Prequel")]
    [DataRow("Book Two", "Book Two")]
    [DataRow("3-1", "3-1")]
    public void CleanPart_PutsAPartInItsCanonicalForm(string input, string expected)
    {
        Assert.AreEqual(expected, SeriesNormalization.CleanPart(input));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void CleanPart_BlankMeansNone(string? input)
    {
        Assert.IsNull(SeriesNormalization.CleanPart(input));
    }

    [TestMethod]
    public void CleanPart_IsIdempotent()
    {
        foreach (var input in new[] { "Book 02.0", "1 to 3", "Prequel", "3-1", "0.50", "12345678901234567890.100" })
        {
            var once = SeriesNormalization.CleanPart(input);
            Assert.AreEqual(once, SeriesNormalization.CleanPart(once), input);
        }
    }

    [TestMethod]
    public void CleanPart_HandlesDigitStringsLongerThanAnyNumericType()
    {
        Assert.AreEqual("12345678901234567890123.5", SeriesNormalization.CleanPart("0012345678901234567890123.50"));
    }

    [TestMethod]
    public void LeadingNumber_IsTheNumberOrTheStartOfARange()
    {
        Assert.AreEqual(2.5m, SeriesNormalization.LeadingNumber("Book 2.50"));
        Assert.AreEqual(1m, SeriesNormalization.LeadingNumber("1-3"));
        Assert.IsNull(SeriesNormalization.LeadingNumber("Prequel"));
        Assert.IsNull(SeriesNormalization.LeadingNumber(null));
        Assert.IsTrue(SeriesNormalization.IsRange("01 to 03"));
        Assert.IsFalse(SeriesNormalization.IsRange("3-1"));
        Assert.IsFalse(SeriesNormalization.IsRange("2"));
    }

    [TestMethod]
    public void CleanName_TrimsAndTreatsBlankAsNone()
    {
        Assert.AreEqual("Mistborn", SeriesNormalization.CleanName("  Mistborn "));
        Assert.IsNull(SeriesNormalization.CleanName("   "));
        Assert.IsNull(SeriesNormalization.CleanName(null));
    }

    // The folder name and the title tags are built from the canonical part by padding it at write
    // time. For the spellings that already were canonical, canonicalising must not move a folder.
    [TestMethod]
    [DataRow("2")]
    [DataRow("12")]
    [DataRow("2.5")]
    [DataRow("1-3")]
    [DataRow("10-12")]
    public void PadSeriesPart_OfAnAlreadyCanonicalPart_IsUnchangedByCanonicalising(string part)
    {
        Assert.AreEqual(AudiobookTagHandler.PadSeriesPart(part), AudiobookTagHandler.PadSeriesPart(SeriesNormalization.CleanPart(part)));
    }

    // A range is "01-03" in the folder (Audiobookshelf cannot read a sequence from it, but this is
    // what has always been written) and a single number never gains a range-looking shape.
    [TestMethod]
    public void PadSeriesPart_OfCanonicalParts_IsWhatTheFolderAlreadyUses()
    {
        Assert.AreEqual("01-03", AudiobookTagHandler.PadSeriesPart(SeriesNormalization.CleanPart("1 to 3")));
        Assert.AreEqual("02", AudiobookTagHandler.PadSeriesPart(SeriesNormalization.CleanPart("Book 2.0")));
        Assert.AreEqual("02.5", AudiobookTagHandler.PadSeriesPart(SeriesNormalization.CleanPart("2.50")));
    }

    [TestMethod]
    public void SeriesPartEquivalence_TreatsSpellingsOfOnePartAsEqual_AndARangeAsItsOwnPart()
    {
        Assert.IsTrue(SeriesPartEquivalence.PartsEquivalentClr("Book 2", "02.0"));
        Assert.IsTrue(SeriesPartEquivalence.PartsEquivalentClr("1-3", "01 to 03"));
        Assert.IsFalse(SeriesPartEquivalence.PartsEquivalentClr("1-3", "1"));
        Assert.IsFalse(SeriesPartEquivalence.PartsEquivalentClr("1-3", "1-4"));
        Assert.IsFalse(SeriesPartEquivalence.PartsEquivalentClr("", "1"));
        Assert.IsTrue(SeriesPartEquivalence.PartsEquivalentClr("Prequel", "prequel"));
    }

    [TestMethod]
    public void SeriesPartSortKey_FilesARangeAtItsFirstBook()
    {
        Assert.AreEqual(1d, SeriesPartSortKey.KeyPlain("1-3"));
        Assert.AreEqual(1d, SeriesPartSortKey.KeyPlain("01 to 03"));
        Assert.AreEqual(SeriesPartSortKey.NonNumericTier, SeriesPartSortKey.KeyPlain("3-1"));
        Assert.AreEqual(SeriesPartSortKey.NonNumericTier, SeriesPartSortKey.KeyPlain("Prequel"));
    }
}
