using AudiobookManager.Domain;

namespace AudiobookManager.Test.Services;

[TestClass]
public class InitialsSpacingFormatterTests
{
    [TestMethod]
    [DataRow("J.K. Rowling", InitialsSpacing.Unspaced, "J.K. Rowling")]
    [DataRow("J. K. Rowling", InitialsSpacing.Unspaced, "J.K. Rowling")]
    [DataRow("J. K. Rowling", InitialsSpacing.Spaced, "J. K. Rowling")]
    [DataRow("J.K. Rowling", InitialsSpacing.Spaced, "J. K. Rowling")]
    [DataRow("J. R. R. Tolkien", InitialsSpacing.Unspaced, "J.R.R. Tolkien")]
    [DataRow("J.R.R. Tolkien", InitialsSpacing.Spaced, "J. R. R. Tolkien")]
    [DataRow("George R. R. Martin", InitialsSpacing.Unspaced, "George R.R. Martin")]
    [DataRow("George R.R. Martin", InitialsSpacing.Spaced, "George R. R. Martin")]
    [DataRow("Rowling", InitialsSpacing.Spaced, "Rowling")]
    [DataRow("Rowling", InitialsSpacing.Unspaced, "Rowling")]
    [DataRow("Brandon Sanderson", InitialsSpacing.Unspaced, "Brandon Sanderson")]
    // One initial before a real word: the space between it and the word is not governed.
    [DataRow("H. Rider Haggard", InitialsSpacing.Unspaced, "H. Rider Haggard")]
    // A multi-letter word ending in a period is not a single-letter initial; leave it alone.
    [DataRow("St. John", InitialsSpacing.Unspaced, "St. John")]
    [DataRow("P. D. James", InitialsSpacing.Unspaced, "P.D. James")]
    // Names without any word following the initials.
    [DataRow("J. K.", InitialsSpacing.Unspaced, "J.K.")]
    public void Format_MapsToCanonicalSpacing(string name, InitialsSpacing spacing, string expected)
    {
        Assert.AreEqual(expected, InitialsSpacingFormatter.Format(name, spacing));
    }

    [TestMethod]
    [DataRow("J.K. Rowling", InitialsSpacing.Unspaced, true)]
    [DataRow("J. K. Rowling", InitialsSpacing.Unspaced, false)]
    [DataRow("J. K. Rowling", InitialsSpacing.Spaced, true)]
    [DataRow("J.K. Rowling", InitialsSpacing.Spaced, false)]
    [DataRow("George R.R. Martin", InitialsSpacing.Unspaced, true)]
    [DataRow("Rowling", InitialsSpacing.Spaced, true)]
    public void IsCompliant_ReportsOnlyTheSpacingMismatch(string name, InitialsSpacing spacing, bool expected)
    {
        Assert.AreEqual(expected, InitialsSpacingFormatter.IsCompliant(name, spacing));
    }
}