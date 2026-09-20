using AudiobookManager.Domain;

namespace AudiobookManager.Test.Services;

[TestClass]
public class InitialsSpacingFormatterTests
{
    [TestMethod]
    [DataRow("J.K. Rowling", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "J.K. Rowling")]
    [DataRow("J. K. Rowling", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "J.K. Rowling")]
    [DataRow("J. K. Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "J. K. Rowling")]
    [DataRow("J.K. Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "J. K. Rowling")]
    [DataRow("J. R. R. Tolkien", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "J.R.R. Tolkien")]
    [DataRow("J.R.R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "J. R. R. Tolkien")]
    [DataRow("George R. R. Martin", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "George R.R. Martin")]
    [DataRow("George R.R. Martin", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "George R. R. Martin")]
    [DataRow("Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "Rowling")]
    [DataRow("Rowling", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "Rowling")]
    [DataRow("Brandon Sanderson", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "Brandon Sanderson")]
    // One initial before a real word: the space between it and the word is not governed.
    [DataRow("H. Rider Haggard", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "H. Rider Haggard")]
    // A multi-letter word ending in a period is not a single-letter initial; leave it alone.
    [DataRow("St. John", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "St. John")]
    [DataRow("P. D. James", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "P.D. James")]
    // Names without any word following the initials.
    [DataRow("J. K.", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, "J.K.")]
    // Undotted punctuation: strips periods from an already-dotted name, or leaves a bare-letter
    // name alone.
    [DataRow("J. R. R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J.R.R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J R R Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J R R Tolkien", InitialsSpacing.Unspaced, InitialsPunctuation.Undotted, "JRR Tolkien")]
    [DataRow("JRR Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J.R.R. Tolkien", InitialsSpacing.Unspaced, InitialsPunctuation.Undotted, "JRR Tolkien")]
    [DataRow("Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "Rowling")]
    // Combining a punctuation change back to dotted from a bare-letter, unspaced name.
    [DataRow("JK Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "J. K. Rowling")]
    public void Format_MapsToCanonicalSpacingAndPunctuation(
        string name, InitialsSpacing spacing, InitialsPunctuation punctuation, string expected)
    {
        Assert.AreEqual(expected, InitialsSpacingFormatter.Format(name, spacing, punctuation));
    }

    [TestMethod]
    [DataRow("J.K. Rowling", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, true)]
    [DataRow("J. K. Rowling", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, false)]
    [DataRow("J. K. Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, true)]
    [DataRow("J.K. Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, false)]
    [DataRow("George R.R. Martin", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, true)]
    [DataRow("Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, true)]
    [DataRow("J R R Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, true)]
    [DataRow("J. R. R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, false)]
    [DataRow("J. R. R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, true)]
    public void IsCompliant_ReportsOnlyTheSpacingOrPunctuationMismatch(
        string name, InitialsSpacing spacing, InitialsPunctuation punctuation, bool expected)
    {
        Assert.AreEqual(expected, InitialsSpacingFormatter.IsCompliant(name, spacing, punctuation));
    }
}
