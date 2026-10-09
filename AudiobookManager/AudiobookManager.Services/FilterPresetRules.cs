using System.Globalization;
using System.Text.Json;

namespace AudiobookManager.Services;

/// <summary>The lists that can hold filter presets; the value is what the API and the table store.</summary>
public static class FilterPresetScopes
{
    public const string Books = "books";
    public const string Series = "series";
    public const string Authors = "authors";

    public static readonly IReadOnlyList<string> All = [Books, Series, Authors];
}

/// <summary>What shape a filter value must have.</summary>
public enum FilterValueKind
{
    StringList,
    Bool,
    NonNegativeInt,
    Instant,
}

/// <summary>
/// The limits and the per-list filter vocabulary a saved preset is validated against. A preset is
/// the query string of a list, saved: <see cref="Keys"/> names exactly the filter parameters each
/// list's endpoint accepts (a test pins that against the controller actions, so a filter added to
/// an endpoint without being added here - or the reverse - fails the build rather than saving
/// presets that silently do nothing). The search text is not a filter and is not saved.
/// </summary>
public static class FilterPresetRules
{
    /// <summary>The bound on a list's presets - the list is returned whole, never paged.</summary>
    public const int MaxPresetsPerScope = 50;

    public const int MaxNameLength = 80;
    public const int MaxListItems = 50;
    public const int MaxListItemLength = 200;
    public const int MaxSerializedFiltersBytes = 16 * 1024;

    private static readonly IReadOnlyDictionary<string, FilterValueKind> Shared = new Dictionary<string, FilterValueKind>
    {
        ["sources"] = FilterValueKind.StringList,
        ["queueStates"] = FilterValueKind.StringList,
        ["refreshedAfter"] = FilterValueKind.Instant,
        ["refreshedBefore"] = FilterValueKind.Instant,
        ["neverRefreshed"] = FilterValueKind.Bool,
    };

    private static IReadOnlyDictionary<string, FilterValueKind> With(params (string Key, FilterValueKind Kind)[] extra)
    {
        var map = new Dictionary<string, FilterValueKind>(Shared);
        foreach (var (key, kind) in extra)
        {
            map[key] = kind;
        }

        return map;
    }

    /// <summary>The filter keys (and value shapes) each list accepts.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, FilterValueKind>> Keys =
        new Dictionary<string, IReadOnlyDictionary<string, FilterValueKind>>
        {
            [FilterPresetScopes.Books] = With(
                ("genres", FilterValueKind.StringList),
                ("languages", FilterValueKind.StringList),
                ("qualifiers", FilterValueKind.StringList),
                ("minDurationInSeconds", FilterValueKind.NonNegativeInt),
                ("maxDurationInSeconds", FilterValueKind.NonNegativeInt)),
            [FilterPresetScopes.Series] = With(
                ("followed", FilterValueKind.Bool),
                ("hasMissingBooks", FilterValueKind.Bool),
                ("hasUpcomingBooks", FilterValueKind.Bool),
                ("minOwnedBooks", FilterValueKind.NonNegativeInt),
                ("maxOwnedBooks", FilterValueKind.NonNegativeInt)),
            [FilterPresetScopes.Authors] = With(
                ("followed", FilterValueKind.Bool),
                ("hasMissingBooks", FilterValueKind.Bool),
                ("hasUpcomingBooks", FilterValueKind.Bool),
                ("minBookCount", FilterValueKind.NonNegativeInt),
                ("maxBookCount", FilterValueKind.NonNegativeInt)),
        };

    // A preset that violates one of these would be refused by the list endpoint it feeds (400),
    // so it is refused here instead of being saved as a preset that can never be applied.
    private static readonly (string Min, string Max)[] RangePairs =
    [
        ("minDurationInSeconds", "maxDurationInSeconds"),
        ("minOwnedBooks", "maxOwnedBooks"),
        ("minBookCount", "maxBookCount"),
        ("refreshedAfter", "refreshedBefore"),
    ];

    public static string NormalizeScope(string? scope)
    {
        var match = FilterPresetScopes.All.FirstOrDefault(s => string.Equals(s, scope?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException(
            $"'{scope}' is not a list that supports presets. Use one of: {string.Join(", ", FilterPresetScopes.All)}.");
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A preset needs a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"A preset name can be at most {MaxNameLength} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A preset name cannot contain control characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Validates the filters against the list's vocabulary and returns them normalized as the JSON
    /// object that is stored: unknown keys are refused (not dropped - a typo should be told),
    /// empty lists are dropped, instants become UTC ISO 8601, keys are in a fixed order. Throws
    /// <see cref="ArgumentException"/> for anything invalid and for a filter set that ends up
    /// empty - a preset of "no filters" would be a way to clear them, not something to save.
    /// </summary>
    public static string NormalizeFilters(string scope, IReadOnlyDictionary<string, JsonElement>? filters)
    {
        var allowed = Keys[scope];
        var normalized = new SortedDictionary<string, object>(StringComparer.Ordinal);

        foreach (var (key, value) in filters ?? new Dictionary<string, JsonElement>())
        {
            if (!allowed.TryGetValue(key, out var kind))
            {
                throw new ArgumentException($"'{key}' is not a filter of the {scope} list.");
            }

            var parsed = ParseValue(key, kind, value);
            if (parsed is not null)
            {
                normalized[key] = parsed;
            }
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException("There are no active filters to save. Set at least one filter first.");
        }

        foreach (var (min, max) in RangePairs)
        {
            if (normalized.TryGetValue(min, out var lower) && normalized.TryGetValue(max, out var upper)
                && Compare(lower, upper) > 0)
            {
                throw new ArgumentException($"'{min}' must not be greater than '{max}'.");
            }
        }

        var json = JsonSerializer.Serialize(normalized);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxSerializedFiltersBytes)
        {
            throw new ArgumentException("The filters of a preset are too large to save.");
        }

        return json;
    }

    /// <summary>Reads stored filters back; a corrupt value (a hand-edited database) reads as no filters.</summary>
    public static IReadOnlyDictionary<string, JsonElement> ParseStored(string filtersJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(filtersJson)
                ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private static int Compare(object lower, object upper) => lower switch
    {
        int a when upper is int b => a.CompareTo(b),
        // Instants are fixed-width UTC strings, so ordinal order is chronological order.
        string a when upper is string b => string.CompareOrdinal(a, b),
        _ => 0,
    };

    private static object? ParseValue(string key, FilterValueKind kind, JsonElement value)
    {
        switch (kind)
        {
            case FilterValueKind.StringList:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw new ArgumentException($"'{key}' must be a list of values.");
                }

                var items = new List<string>();
                foreach (var element in value.EnumerateArray())
                {
                    var text = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim() : null;
                    if (string.IsNullOrEmpty(text) || text.Length > MaxListItemLength)
                    {
                        throw new ArgumentException($"'{key}' holds a value that is blank or longer than {MaxListItemLength} characters.");
                    }

                    if (!items.Contains(text, StringComparer.Ordinal))
                    {
                        items.Add(text);
                    }
                }

                if (items.Count > MaxListItems)
                {
                    throw new ArgumentException($"'{key}' can hold at most {MaxListItems} values.");
                }

                return items.Count == 0 ? null : items;

            case FilterValueKind.Bool:
                return value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw new ArgumentException($"'{key}' must be true or false."),
                };

            case FilterValueKind.NonNegativeInt:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
                {
                    throw new ArgumentException($"'{key}' must be a whole number, zero or greater.");
                }

                return number;

            case FilterValueKind.Instant:
                if (value.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(
                        value.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
                {
                    throw new ArgumentException($"'{key}' must be a date and time.");
                }

                return instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
