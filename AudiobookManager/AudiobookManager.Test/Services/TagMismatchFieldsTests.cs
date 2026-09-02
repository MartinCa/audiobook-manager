using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// TagMismatchFields.ApplyValue is the resolve-side inverse of TagConsistencyChecker's
/// serialization: a value the dialog shows (and the user picks) must map back onto the domain
/// object that AudiobookService.UpdateAudiobook persists, and the round-trip verification that
/// gates a save has to agree with what was written. These tests pin that contract, including the
/// structural-field guard and the serialized-genre round-trip.
/// </summary>
[TestClass]
public class TagMismatchFieldsTests
{
    private static Audiobook MakeBook() =>
        new(
            new List<Person> { new Person("Author") },
            "A Book",
            2024,
            new AudiobookFileInfo("/library/book.m4b", "book.m4b", 1000));

    [TestMethod]
    public void ApplyValue_PersonField_NullClears()
    {
        var book = MakeBook();

        // Narrators is non-structural (it does not drive the library path), so a null clear is allowed.
        TagMismatchFields.ApplyValue(book, "Narrators", null);

        Assert.AreEqual(0, book.Narrators.Count);
    }

    [TestMethod]
    public void ApplyValue_PersonField_ParsesCommaSeparated()
    {
        var book = MakeBook();

        TagMismatchFields.ApplyValue(book, "Narrators", "Alice, Bob");

        Assert.AreEqual(2, book.Narrators.Count);
        Assert.AreEqual("Alice", book.Narrators[0].Name);
        Assert.AreEqual("Bob", book.Narrators[1].Name);
    }

    // The genres delimiter in the m4b tag is '/', but the serialized value the dialog round-trips
    // is joined with ", " (TagConsistencyChecker.FormatGenres). Splitting on '/' here would turn
    // the user's "Fantasy, Sci-Fi" file/library choice into one genre named "Fantasy, Sci-Fi" -
    // always matched by the checker afterwards (same serializer on both sides) but wrong: a
    // real multi-genre book collapses into a single genre. ParseGenres is the exact inverse.
    [TestMethod]
    public void ApplyValue_Genres_SplitsOnCommaSpaceNotSlash()
    {
        var book = MakeBook();

        TagMismatchFields.ApplyValue(book, "Genres", "Fantasy, Sci-Fi");

        CollectionAssert.AreEqual(new[] { "Fantasy", "Sci-Fi" }, book.Genres);
        // And the file's raw '/'-separated tag still parses to the same list.
        CollectionAssert.AreEqual(book.Genres, AudiobookManager.FileManager.AudiobookTagHandler.ParseGenresFromString("Fantasy/Sci-Fi"));
    }

    [TestMethod]
    public void ApplyValue_Year_ParsesNumeric()
    {
        var book = MakeBook();

        TagMismatchFields.ApplyValue(book, "Year", "1999");

        Assert.AreEqual(1999, book.Year);
    }

    [TestMethod]
    public void ApplyValue_Year_NonNumericClears()
    {
        var book = MakeBook();

        TagMismatchFields.ApplyValue(book, "Year", "not-a-year");

        Assert.IsNull(book.Year);
    }

    [TestMethod]
    public void ApplyValue_StringField_EmptyClears()
    {
        var book = MakeBook();

        // Non-nullable domain string: empty becomes null, same as the "clear" intent.
        TagMismatchFields.ApplyValue(book, "Subtitle", "");

        Assert.IsNull(book.Subtitle);
    }

    // null and "" are the same wire value for a clear; the server cannot tell "use the library
    // value, which happens to be empty" from "clear this field". They must stay equivalent.
    [TestMethod]
    public void ApplyValue_NullAndEmpty_AreEquivalentClears()
    {
        var withNull = MakeBook();
        var withEmpty = MakeBook();

        TagMismatchFields.ApplyValue(withNull, "Description", null);
        TagMismatchFields.ApplyValue(withEmpty, "Description", "");

        Assert.IsNull(withNull.Description);
        Assert.IsNull(withEmpty.Description);
    }

    [TestMethod]
    public void ApplyValue_UnknownField_ThrowsArgumentOutOfRange()
    {
        var book = MakeBook();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => TagMismatchFields.ApplyValue(book, "NotAField", "x"));
    }

    [TestMethod]
    public void ApplyValue_StructuralField_RejectsEmpty()
    {
        foreach (var field in new[] { "Author", "Book Name", "Year" })
        {
            var book = MakeBook();
            var ex = Assert.ThrowsExactly<ArgumentException>(
                () => TagMismatchFields.ApplyValue(book, field, null));
            StringAssert.Contains(ex.Message, $"Field '{field}'");
            StringAssert.Contains(ex.Message, "cannot be cleared");
        }
    }

    [TestMethod]
    public void ApplyValue_StructuralField_NonEmptyIsAllowed()
    {
        var book = MakeBook();

        TagMismatchFields.ApplyValue(book, "Book Name", "Renamed");
        TagMismatchFields.ApplyValue(book, "Year", "2030");
        TagMismatchFields.ApplyValue(book, "Author", "New Author");

        Assert.AreEqual("Renamed", book.BookName);
        Assert.AreEqual(2030, book.Year);
        Assert.AreEqual("New Author", book.Authors.Single().Name);
    }
}