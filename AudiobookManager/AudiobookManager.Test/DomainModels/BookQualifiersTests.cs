using AudiobookManager.Domain;

namespace AudiobookManager.Test.DomainModels;

[TestClass]
public class BookQualifiersTests
{
    private static Audiobook Parsed(string name, string? series = null) =>
        new(new List<Person> { new("Lee Child") }, name, 1997, new AudiobookFileInfo("/library/book.m4b", "book.m4b", 1000))
        {
            Series = series
        };

    // ---- Normalize ----

    [TestMethod]
    public void Normalize_SortsAlphabeticallyByLabelAndDeduplicates()
    {
        var result = BookQualifiers.Normalize(new[] { "Dramatized", "abridged", " DRAMATIZED ", "", null });

        CollectionAssert.AreEqual(new List<string> { "abridged", "dramatized" }, result);
    }

    // A key without a label can never be written into a name and read back, so keeping one would
    // leave the book permanently mismatched (and un-saveable) once a qualifier is retired.
    [TestMethod]
    public void Normalize_DropsAKeyTheRegistryDoesNotKnow()
    {
        var result = BookQualifiers.Normalize(new[] { "zzz-retired", "dramatized" });

        CollectionAssert.AreEqual(new List<string> { "dramatized" }, result);
    }

    [TestMethod]
    public void Format_AndParse_NeverCarryAnUnknownKey()
    {
        Assert.AreEqual("Dramatized", BookQualifiers.Format(new[] { "zzz-retired", "dramatized" }));
        CollectionAssert.AreEqual(new List<string> { "dramatized" }, BookQualifiers.Parse("Dramatized, Retired Label"));
    }

    [TestMethod]
    public void ApplyExpected_ABookStoringARetiredKeyStillRoundTripsWithoutAMismatch()
    {
        // Stored set has a retired key; the file was written from the normalized set (no suffix
        // for it). Comparing the stored set to the reshaped parse must agree.
        var stored = new[] { "zzz-retired", "dramatized" };
        var parsed = Parsed(BookQualifiers.Apply("Killing Floor", stored)!);

        BookQualifiers.ApplyExpected(parsed, stored);

        Assert.AreEqual("Killing Floor", parsed.BookName);
        Assert.AreEqual(BookQualifiers.Format(stored), BookQualifiers.Format(parsed.Qualifiers));
    }

    [TestMethod]
    public void Normalize_NullIsEmpty()
    {
        Assert.AreEqual(0, BookQualifiers.Normalize(null).Count);
    }

    // ---- Apply ----

    [TestMethod]
    public void Apply_AppendsOneParentheticalPerQualifier()
    {
        Assert.AreEqual("Killing Floor (Dramatized)", BookQualifiers.Apply("Killing Floor", new[] { "dramatized" }));
    }

    [TestMethod]
    public void Apply_MultipleQualifiersAreOrderedAlphabeticallyWhateverTheInputOrder()
    {
        Assert.AreEqual(
            "Killing Floor (Abridged) (Dramatized)",
            BookQualifiers.Apply("Killing Floor", new[] { "dramatized", "abridged" }));
    }

    [TestMethod]
    public void Apply_NoQualifiersLeavesTheNameUntouched()
    {
        Assert.AreEqual("Killing Floor", BookQualifiers.Apply("Killing Floor", Array.Empty<string>()));
    }

    [TestMethod]
    public void Apply_AnUnknownKeyContributesNoSuffix()
    {
        Assert.AreEqual("Killing Floor", BookQualifiers.Apply("Killing Floor", new[] { "zzz-retired" }));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Apply_ABlankNameIsReturnedUnchanged(string? name)
    {
        Assert.AreEqual(name, BookQualifiers.Apply(name, new[] { "dramatized" }));
    }

    // ---- ApplyExpected ----

    [TestMethod]
    public void ApplyExpected_StripsTheSuffixesOfTheBooksStoredQualifiers()
    {
        var parsed = Parsed("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual("Killing Floor", parsed.BookName);
        Assert.AreEqual("Jack Reacher", parsed.Series);
        CollectionAssert.AreEqual(new List<string> { "dramatized" }, parsed.Qualifiers);
    }

    [TestMethod]
    public void ApplyExpected_StripsSeveralQualifiers()
    {
        var parsed = Parsed("Killing Floor (Abridged) (Dramatized)", "Jack Reacher (Abridged) (Dramatized)");

        BookQualifiers.ApplyExpected(parsed, new[] { "abridged", "dramatized" });

        Assert.AreEqual("Killing Floor", parsed.BookName);
        Assert.AreEqual("Jack Reacher", parsed.Series);
        CollectionAssert.AreEqual(new List<string> { "abridged", "dramatized" }, parsed.Qualifiers);
    }

    // The point of "only if the book has the related qualifier": a book stored without qualifiers
    // is never reshaped, so a title that merely ends in a qualifier's label keeps it.
    [TestMethod]
    public void ApplyExpected_ABookWithNoStoredQualifiersIsNeverSplit()
    {
        var parsed = Parsed("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)");

        BookQualifiers.ApplyExpected(parsed, Array.Empty<string>());

        Assert.AreEqual("Killing Floor (Dramatized)", parsed.BookName);
        Assert.AreEqual("Jack Reacher (Dramatized)", parsed.Series);
        Assert.AreEqual(0, parsed.Qualifiers.Count);
    }

    [TestMethod]
    public void ApplyExpected_OnlyTheStoredQualifiersAreStripped()
    {
        var parsed = Parsed("Killing Floor (Abridged) (Dramatized)");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual("Killing Floor (Abridged)", parsed.BookName, "an unstored suffix is part of the title as far as the file is concerned");
        CollectionAssert.AreEqual(new List<string> { "dramatized" }, parsed.Qualifiers);
    }

    [TestMethod]
    public void ApplyExpected_AFileMissingAStoredSuffixIsLeftForTheMismatchToSurface()
    {
        var parsed = Parsed("Killing Floor");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual("Killing Floor", parsed.BookName);
        Assert.AreEqual(0, parsed.Qualifiers.Count, "nothing was stripped, so the stored qualifier is missing from the file");
    }

    [TestMethod]
    public void ApplyExpected_ASeriesThatLostItsSuffixLeavesTheWholeBookUntouched()
    {
        var parsed = Parsed("Killing Floor (Dramatized)", "Jack Reacher");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual("Killing Floor (Dramatized)", parsed.BookName);
        Assert.AreEqual("Jack Reacher", parsed.Series);
        Assert.AreEqual(0, parsed.Qualifiers.Count);
    }

    [TestMethod]
    public void ApplyExpected_ABookWithoutASeriesOnlyNeedsTheNameSuffix()
    {
        var parsed = Parsed("Killing Floor (Dramatized)");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual("Killing Floor", parsed.BookName);
        Assert.IsNull(parsed.Series);
    }

    [TestMethod]
    public void ApplyExpected_SuffixesOutOfCanonicalOrderAreNotStripped()
    {
        var parsed = Parsed("Killing Floor (Dramatized) (Abridged)");

        BookQualifiers.ApplyExpected(parsed, new[] { "abridged", "dramatized" });

        Assert.AreEqual("Killing Floor (Dramatized) (Abridged)", parsed.BookName);
        Assert.AreEqual(0, parsed.Qualifiers.Count, "a file written in a non-canonical order must read as a difference, not as consistent");
    }

    [TestMethod]
    public void ApplyExpected_IsCaseInsensitiveOnTheLabel()
    {
        var parsed = Parsed("Killing Floor (dramatized)");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual("Killing Floor", parsed.BookName);
    }

    [TestMethod]
    public void ApplyExpected_NeverStripsTheWholeName()
    {
        var parsed = Parsed(" (Dramatized)");

        BookQualifiers.ApplyExpected(parsed, new[] { "dramatized" });

        Assert.AreEqual(" (Dramatized)", parsed.BookName);
        Assert.AreEqual(0, parsed.Qualifiers.Count);
    }

    [TestMethod]
    public void ApplyExpected_IsTheInverseOfApply()
    {
        foreach (var keys in new[] { new[] { "abridged" }, new[] { "dramatized" }, new[] { "abridged", "dramatized" } })
        {
            var parsed = Parsed(
                BookQualifiers.Apply("Killing Floor", keys)!,
                BookQualifiers.Apply("Jack Reacher", keys));

            BookQualifiers.ApplyExpected(parsed, keys);

            Assert.AreEqual("Killing Floor", parsed.BookName);
            Assert.AreEqual("Jack Reacher", parsed.Series);
            CollectionAssert.AreEqual(keys.ToList(), parsed.Qualifiers);
        }
    }

    // ---- Format / Parse ----

    [TestMethod]
    public void Format_ThenParse_RoundTrips()
    {
        var formatted = BookQualifiers.Format(new[] { "dramatized", "abridged" });

        Assert.AreEqual("Abridged, Dramatized", formatted);
        CollectionAssert.AreEqual(new List<string> { "abridged", "dramatized" }, BookQualifiers.Parse(formatted));
    }

    [TestMethod]
    public void Parse_EmptyIsNoQualifiers()
    {
        Assert.AreEqual(0, BookQualifiers.Parse("").Count);
        Assert.AreEqual(0, BookQualifiers.Parse(null).Count);
    }

    // ---- Registry ----

    [TestMethod]
    public void EveryRegisteredQualifierHasAUniqueKeyAndLabelAndAKeyThatSurvivesTheColumnFormat()
    {
        Assert.AreEqual(BookQualifiers.All.Count, BookQualifiers.All.Select(q => q.Key).Distinct().Count());
        Assert.AreEqual(BookQualifiers.All.Count, BookQualifiers.All.Select(q => q.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var qualifier in BookQualifiers.All)
        {
            Assert.AreEqual(qualifier.Key, qualifier.Key.Trim().ToLowerInvariant(), "keys are stored lowercased");
            Assert.IsFalse(qualifier.Key.Contains(','), "the column is comma delimited");
            Assert.IsFalse(string.IsNullOrWhiteSpace(qualifier.Label));
        }
    }
}
