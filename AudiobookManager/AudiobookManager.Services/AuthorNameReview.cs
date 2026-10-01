using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// Decides whether the name a metadata source reports for an author is worth proposing as a
/// rename of the library's own spelling.
///
/// Both names are first formatted to the library's initials convention
/// (<see cref="InitialsSpacingFormatter"/>), the same canonical form the initials-spacing
/// consistency check validates stored names against. A source rarely follows the library's
/// convention (Hardcover says "J. K. Rowling" where a library set to unspaced dotted initials
/// stores "J.K. Rowling"), and offering that as a "rename" would be noise the user can only
/// dismiss - and the accepted name would then trip the consistency check. Anything that still
/// differs after formatting - another spelling, case, accents, a different surname - is a real
/// difference.
/// </summary>
public static class AuthorNameReview
{
    /// <summary>
    /// The name to propose - the source's name formatted to the library's convention - or null
    /// when the source reports no name or it agrees with <paramref name="storedName"/> once both
    /// follow the convention.
    /// </summary>
    public static string? ProposeRename(
        string storedName,
        string? sourceName,
        InitialsSpacing spacing,
        InitialsPunctuation punctuation)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        var proposed = InitialsSpacingFormatter.Format(sourceName.Trim(), spacing, punctuation);
        if (proposed.Length == 0)
        {
            return null;
        }

        var current = InitialsSpacingFormatter.Format(storedName.Trim(), spacing, punctuation);
        return string.Equals(proposed, current, StringComparison.Ordinal) ? null : proposed;
    }
}
