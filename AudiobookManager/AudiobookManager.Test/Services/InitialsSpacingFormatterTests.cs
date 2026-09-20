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
    // run of individual-letter tokens alone.
    [DataRow("J. R. R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J.R.R. Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J R R Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "J R R Tolkien")]
    [DataRow("J R R Tolkien", InitialsSpacing.Unspaced, InitialsPunctuation.Undotted, "JRR Tolkien")]
    // Output CAN concatenate bare letters into "JRR" (from certain, dotted input), but parsing an
    // already-concatenated bare token like "JRR" back out is deliberately not supported - see
    // ClassifyInitialTokens's doc comment. It round-trips unchanged instead of being reformatted.
    [DataRow("JRR Tolkien", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "JRR Tolkien")]
    [DataRow("J.R.R. Tolkien", InitialsSpacing.Unspaced, InitialsPunctuation.Undotted, "JRR Tolkien")]
    [DataRow("Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Undotted, "Rowling")]
    // A multi-letter bare token, even one produced by a prior Unspaced+Undotted resolve, is never
    // parsed back into initials - it round-trips unchanged rather than being reformatted.
    [DataRow("JK Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "JK Rowling")]
    // Regression guard (PR review): an earlier version treated any all-uppercase token as an
    // initials run, which corrupted real names. None of these may be rewritten.
    [DataRow("John Smith III", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "John Smith III")]
    [DataRow("JOHN SMITH", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "JOHN SMITH")]
    [DataRow("A Tale of Two Cities", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "A Tale of Two Cities")]
    // A lone bare letter with no neighboring initial is left alone even under Undotted, since it
    // is indistinguishable from a real one-letter word.
    [DataRow("H Rider Haggard", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "H Rider Haggard")]
    // But a bare letter run of two or more, or one next to a dotted initial, still qualifies.
    [DataRow("J K Rowling", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, "J. K. Rowling")]
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
    // Regression guard: these must never be reported as non-compliant, or the consistency check
    // would flag them and a bulk resolve would corrupt them (see PR review).
    [DataRow("John Smith III", InitialsSpacing.Spaced, InitialsPunctuation.Dotted, true)]
    [DataRow("JOHN SMITH", InitialsSpacing.Unspaced, InitialsPunctuation.Undotted, true)]
    [DataRow("A Tale of Two Cities", InitialsSpacing.Unspaced, InitialsPunctuation.Dotted, true)]
    public void IsCompliant_ReportsOnlyTheSpacingOrPunctuationMismatch(
        string name, InitialsSpacing spacing, InitialsPunctuation punctuation, bool expected)
    {
        Assert.AreEqual(expected, InitialsSpacingFormatter.IsCompliant(name, spacing, punctuation));
    }
}
