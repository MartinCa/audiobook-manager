namespace AudiobookManager.Domain;

/// <summary>
/// Whether a name's run of single-letter initials carries a trailing period - the single
/// library-wide preference the initials-spacing compliance check also validates against,
/// alongside <see cref="InitialsSpacing"/>.
///
/// Only whether each initial letter is followed by a period is governed here; the whitespace
/// between adjacent initials is <see cref="InitialsSpacing"/>'s concern. "J. R. R. Tolkien" and
/// "J R R Tolkien" differ under this setting alone.
/// </summary>
public enum InitialsPunctuation
{
    /// <summary>Each initial is followed by a period: "J. R. R. Tolkien".</summary>
    Dotted = 0,

    /// <summary>Initials carry no periods: "J R R Tolkien".</summary>
    Undotted = 1,
}
