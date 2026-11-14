using System.Text.Json;

namespace AudiobookManager.Domain;

/// <summary>
/// Per-source overrides of <see cref="LibrarySettings.SearchInitialsHandling"/>: how author
/// initials are written in the default (and automated) search query sent to one metadata source.
/// Sources index names differently - Hardcover may need "George R.R. Martin" where Audible wants
/// "George R. R. Martin" - so one library-wide choice cannot suit all of them. A source with no
/// entry follows the library-wide setting. Stored as a JSON object of source name to enum name;
/// this is the one implementation of reading, validating and applying it.
/// </summary>
public static class MetadataSourceSearchSettings
{
    /// <summary>The most sources a saved set may name - far above the number of scrapers there will be.</summary>
    public const int MaxEntries = 50;

    /// <summary>The longest a single source name may be.</summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Trims names, drops blank ones and repeats (ignoring case; the first spelling wins).
    /// </summary>
    public static Dictionary<string, SearchInitialsHandling> Normalize(
        IEnumerable<KeyValuePair<string, SearchInitialsHandling>>? entries)
    {
        var result = new Dictionary<string, SearchInitialsHandling>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, handling) in entries ?? [])
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                result.TryAdd(trimmed, handling);
            }
        }

        return result;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> (with a message written for the caller) when
    /// <paramref name="entries"/> is not an acceptable saved set.
    /// </summary>
    public static void Validate(IReadOnlyDictionary<string, SearchInitialsHandling> entries)
    {
        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException($"At most {MaxEntries} sources can have their own initials handling.");
        }

        if (entries.Keys.Any(n => n.Length > MaxNameLength))
        {
            throw new ArgumentException($"A source name can be at most {MaxNameLength} characters.");
        }

        foreach (var (name, handling) in entries)
        {
            if (!Enum.IsDefined(handling))
            {
                throw new ArgumentException($"'{handling}' is not a known search initials handling (source '{name}').");
            }
        }
    }

    /// <summary>
    /// The handling for <paramref name="sourceName"/>: its own entry, otherwise the library-wide
    /// <paramref name="libraryDefault"/>.
    /// </summary>
    public static SearchInitialsHandling Resolve(
        IReadOnlyDictionary<string, SearchInitialsHandling>? overrides,
        string sourceName,
        SearchInitialsHandling libraryDefault)
    {
        if (overrides is null)
        {
            return libraryDefault;
        }

        foreach (var (name, handling) in overrides)
        {
            if (string.Equals(name, sourceName, StringComparison.OrdinalIgnoreCase))
            {
                return handling;
            }
        }

        return libraryDefault;
    }

    /// <summary>The stored form: a JSON object, or null for "no overrides".</summary>
    public static string? Serialize(IReadOnlyDictionary<string, SearchInitialsHandling> entries) =>
        entries.Count == 0
            ? null
            : JsonSerializer.Serialize(entries.ToDictionary(e => e.Key, e => e.Value.ToString()));

    /// <summary>
    /// Reads a stored set. Anything unreadable - bad JSON, or an entry whose value no longer names
    /// a handling - is dropped rather than raised, so the source simply follows the library default.
    /// </summary>
    public static Dictionary<string, SearchInitialsHandling> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
            return Normalize(raw
                .Where(e => Enum.TryParse<SearchInitialsHandling>(e.Value, ignoreCase: true, out var h) && Enum.IsDefined(h))
                .Select(e => KeyValuePair.Create(e.Key, Enum.Parse<SearchInitialsHandling>(e.Value!, ignoreCase: true))));
        }
        catch (JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
