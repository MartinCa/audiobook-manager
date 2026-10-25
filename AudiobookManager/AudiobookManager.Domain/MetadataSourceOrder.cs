using System.Text.Json;

namespace AudiobookManager.Domain;

/// <summary>
/// The user's preferred order of the online metadata sources (Audible, Goodreads, Hardcover, ...),
/// edited in Library Settings and applied wherever sources are listed: the source picker, search
/// result tabs, the pending-match rows, the filter dropdowns. Stored as a plain list of source
/// names; this is the one implementation of reading, validating and applying it.
/// </summary>
public static class MetadataSourceOrder
{
    /// <summary>The most names a saved order may hold - far above the number of scrapers there will ever be.</summary>
    public const int MaxEntries = 50;

    /// <summary>The longest a single source name may be.</summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Trims the names, drops blanks and repeats (ignoring case; the first spelling wins), keeping
    /// the order given.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? names)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names ?? [])
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> (with a message written for the caller) when
    /// <paramref name="names"/> is not an acceptable saved order.
    /// </summary>
    public static void Validate(IReadOnlyList<string> names)
    {
        if (names.Count > MaxEntries)
        {
            throw new ArgumentException($"A source order can list at most {MaxEntries} sources.");
        }

        if (names.Any(n => n.Length > MaxNameLength))
        {
            throw new ArgumentException($"A source name can be at most {MaxNameLength} characters.");
        }
    }

    /// <summary>
    /// Puts <paramref name="items"/> in the saved order. A source the order does not mention (one
    /// added after the order was saved, or never ordered) follows the listed ones, alphabetically,
    /// so the result never depends on the order scrapers happen to be registered in.
    /// </summary>
    public static List<T> Sort<T>(IEnumerable<T> items, Func<T, string> nameOf, IReadOnlyList<string> order)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++)
        {
            rank.TryAdd(order[i], i);
        }

        return items
            .Select(item => (Item: item, Name: nameOf(item)))
            .OrderBy(x => rank.TryGetValue(x.Name, out var r) ? r : int.MaxValue)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Item)
            .ToList();
    }

    public static List<string> Sort(IEnumerable<string> names, IReadOnlyList<string> order) =>
        Sort(names, n => n, order);

    /// <summary>The stored form of an order: a JSON array, or null for "no order chosen".</summary>
    public static string? Serialize(IReadOnlyList<string> names) =>
        names.Count == 0 ? null : JsonSerializer.Serialize(names);

    /// <summary>Reads a stored order; anything unreadable is "no order chosen" rather than an error.</summary>
    public static List<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<List<string?>>(json));
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
