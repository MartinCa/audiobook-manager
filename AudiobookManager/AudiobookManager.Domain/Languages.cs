namespace AudiobookManager.Domain;

/// <summary>
/// The canonical set of languages the library supports, and the normalization that folds the
/// free-text values metadata sources hand back into those codes.
///
/// The stored value is the ISO 639-1 code ("en", "da"). It is what goes into the database, the
/// m4b's language tag and <c>metadata.opf</c>'s <c>dc:language</c> element - the latter is
/// specified as an ISO 639 / RFC 5646 code, so a display name like "English" was never correct
/// there. Scrapers report whatever their source shows ("English" from Goodreads, the "Language:"
/// label text from Audible), and files tagged elsewhere carry anything from "eng" to "Dansk", so
/// every value entering the system goes through <see cref="Normalize"/>.
///
/// This list is deliberately fixed in code rather than configurable: it is exposed to the client
/// over <c>GET /api/settings/languages</c> so the frontend has no list of its own to drift from.
/// </summary>
public static class Languages
{
    public const string DefaultCode = "en";

    /// <summary>Supported languages, in the order they should be offered to a user.</summary>
    public static readonly IReadOnlyList<(string Code, string DisplayName)> Supported = new List<(string, string)>
    {
        ("en", "English"),
        ("da", "Danish"),
    };

    /// <summary>
    /// Every spelling that folds to a supported code. Keys are already lowercased - lookups go
    /// through <see cref="Normalize"/>, which lowercases first.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        // English: ISO 639-1, both ISO 639-2 forms, and the display names sources use.
        ["en"] = "en",
        ["eng"] = "en",
        ["english"] = "en",

        // Danish: ISO 639-1, ISO 639-2/T and /B, the English name and the endonym.
        ["da"] = "da",
        ["dan"] = "da",
        ["danish"] = "da",
        ["dansk"] = "da",
    };

    /// <summary>
    /// Folds a free-text language value to a supported ISO 639-1 code, or returns null when the
    /// value is empty or names a language this library does not manage. Callers decide what an
    /// unrecognized value means: the save path keeps it verbatim so a hand-tagged oddity survives,
    /// while the backfill skips it so the book stays visible in Missing Tags.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().ToLowerInvariant();

        // Region-qualified tags ("en-US", "da_DK") name the same language as their base subtag.
        var separatorIndex = value.IndexOfAny(new[] { '-', '_' });
        if (separatorIndex > 0)
        {
            value = value[..separatorIndex];
        }

        return Aliases.TryGetValue(value, out var code) ? code : null;
    }

    /// <summary>
    /// Every lowercased spelling that folds to <paramref name="code"/>, so the client can fold a
    /// scraped or tagged value exactly the way <see cref="Normalize"/> does. Serving these rather
    /// than reimplementing them in TypeScript is what keeps the two from drifting - the endonym
    /// "Dansk" is in this table and is not derivable from the code or the English display name.
    /// </summary>
    public static List<string> AliasesFor(string code) =>
        Aliases.Where(kvp => string.Equals(kvp.Value, code, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();

    public static bool IsSupported(string? code) =>
        code is not null && Supported.Any(l => string.Equals(l.Code, code, StringComparison.Ordinal));

    /// <summary>
    /// The name to show for a stored code. An unmanaged value is shown as-is rather than hidden -
    /// see the "unrecognized" option the client's language select keeps for exactly this case.
    /// </summary>
    public static string DisplayName(string code)
    {
        foreach (var language in Supported)
        {
            if (string.Equals(language.Code, code, StringComparison.Ordinal))
            {
                return language.DisplayName;
            }
        }

        return code;
    }
}
