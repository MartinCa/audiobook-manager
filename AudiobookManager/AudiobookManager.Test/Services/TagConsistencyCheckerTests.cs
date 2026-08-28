using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// TagConsistencyChecker gates both the consistency check and - since the save pipeline verifies
/// the tag round-trip - whether a book can be saved at all. Any normalization the tag writer
/// applies has to be mirrored here, or a book gets stuck reporting a mismatch that no amount of
/// re-saving can clear.
/// </summary>
[TestClass]
public class TagConsistencyCheckerTests
{
    private static Audiobook MakeBook(
        IEnumerable<string>? authors = null,
        IEnumerable<string>? narrators = null,
        IEnumerable<string>? genres = null,
        string? language = null) =>
        new(
            (authors ?? new[] { "Author" }).Select(a => new Person(a)).ToList(),
            "A Book",
            2024,
            new AudiobookFileInfo("/library/book.m4b", "book.m4b", 1000))
        {
            Narrators = (narrators ?? Array.Empty<string>()).Select(n => new Person(n)).ToList(),
            Genres = (genres ?? Array.Empty<string>()).ToList(),
            Language = language,
        };

    [TestMethod]
    public void FindMismatches_RepeatedAuthorName_IsNotReportedAsAMismatch()
    {
        // Regression: AudiobookTagHandler.GetStringFromListOfPersons de-duplicates before writing,
        // so ["King", "King"] is written as "King" and reads back as ["King"]. The checker did not
        // de-duplicate, so the round-trip verification threw on every save of such a book -
        // permanently, and with a misleading "non-contiguous QuickTime chapters" message.
        var requested = MakeBook(authors: new[] { "Stephen King", "Stephen King" });
        var readBack = MakeBook(authors: new[] { "Stephen King" });

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string>(), mismatches.Select(m => m.Field).ToList());
    }

    [TestMethod]
    public void FindMismatches_RepeatedNarratorName_IsNotReportedAsAMismatch()
    {
        var requested = MakeBook(narrators: new[] { "A Narrator", "A Narrator" });
        var readBack = MakeBook(narrators: new[] { "A Narrator" });

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string>(), mismatches.Select(m => m.Field).ToList());
    }

    [TestMethod]
    public void FindMismatches_BlankGenreEntry_IsNotReportedAsAMismatch()
    {
        // A genre-less book writes an empty genre tag, which reads back as no genres at all.
        var requested = MakeBook(genres: new[] { "" });
        var readBack = MakeBook(genres: Array.Empty<string>());

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string>(), mismatches.Select(m => m.Field).ToList());
    }

    [TestMethod]
    public void FindMismatches_GenuinelyDifferentAuthors_AreStillReported()
    {
        var requested = MakeBook(authors: new[] { "Stephen King" });
        var readBack = MakeBook(authors: new[] { "Peter Straub" });

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string> { "Author" }, mismatches.Select(m => m.Field).ToList());
        Assert.AreEqual("Stephen King", mismatches[0].Expected);
        Assert.AreEqual("Peter Straub", mismatches[0].Actual);
    }

    [TestMethod]
    public void FindMismatches_ExtraAuthorBeyondTheDuplicate_IsStillReported()
    {
        // De-duplication must not swallow a real difference: two distinct authors requested,
        // one written back.
        var requested = MakeBook(authors: new[] { "Stephen King", "Stephen King", "Peter Straub" });
        var readBack = MakeBook(authors: new[] { "Stephen King" });

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string> { "Author" }, mismatches.Select(m => m.Field).ToList());
        Assert.AreEqual("Peter Straub, Stephen King", mismatches[0].Expected);
    }

    [TestMethod]
    public void FindMismatches_GenuinelyDifferentGenres_AreStillReported()
    {
        var requested = MakeBook(genres: new[] { "Fantasy" });
        var readBack = MakeBook(genres: new[] { "Horror" });

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string> { "Genres" }, mismatches.Select(m => m.Field).ToList());
    }

    [TestMethod]
    public void FindMismatches_GenuinelyDifferentLanguage_IsStillReported()
    {
        var requested = MakeBook(language: "English");
        var readBack = MakeBook(language: "German");

        var mismatches = TagConsistencyChecker.FindMismatches(requested, readBack);

        CollectionAssert.AreEqual(new List<string> { "Language" }, mismatches.Select(m => m.Field).ToList());
        Assert.AreEqual("English", mismatches[0].Expected);
        Assert.AreEqual("German", mismatches[0].Actual);
    }
}
