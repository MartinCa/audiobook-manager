namespace AudiobookManager.Domain;

/// <summary>
/// The one canonical form of a series name and part, shared by the relation writer
/// (<c>SeriesRelationSync</c>), the refresh differ (<c>SeriesRelationSet</c>), the repository
/// backstop and mirrored by the client twin (<c>helpers/seriesRelations.ts</c>).
///
/// Deliberately minimal: names and parts are trimmed and blank means "none"; names are the same
/// series when they match case-insensitively (the first spelling wins). A part is <b>not</b>
/// rewritten (<c>Book 2</c> / <c>2.0</c> stay as written) because it reaches the m4b tags and the
/// library path - equivalence between spellings is a comparison concern
/// (<c>SeriesPartEquivalence</c>), not a stored-value one.
/// </summary>
public static class SeriesNormalization
{
    /// <summary>Case rule for "the same series": ordinal, ignoring case.</summary>
    public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>The name trimmed; null when it is null or blank.</summary>
    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>The part trimmed; null when it is null or blank.</summary>
    public static string? CleanPart(string? part) => CleanName(part);
}
