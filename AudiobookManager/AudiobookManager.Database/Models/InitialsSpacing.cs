namespace AudiobookManager.Database.Models;

/// <summary>
/// How a name's run of dotted single-letter initials is spaced in the library - the persisted
/// value of the initials-spacing library setting. Mirrored by AudiobookManager.Domain's
/// InitialsSpacing; the service layer maps between the two, the way it does for Person and
/// SeriesMapping.
///
/// Only the whitespace BETWEEN two adjacent initials is governed. The space between the last
/// initial and the following word is always a single space, whatever this value is: "J.K. Rowling"
/// and "J. K. Rowling" differ under this setting, but "J.K.Rowling" is never the canonical form of
/// either.
/// </summary>
public enum InitialsSpacing
{
    /// <summary>One space between adjacent initials: "J. K. Rowling".</summary>
    Spaced = 0,

    /// <summary>No space between adjacent initials: "J.K. Rowling".</summary>
    Unspaced = 1,
}
