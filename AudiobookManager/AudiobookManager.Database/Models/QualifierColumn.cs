namespace AudiobookManager.Database.Models;

/// <summary>
/// (De)serializes the <see cref="Audiobook.Qualifiers"/> column: the qualifier keys, comma
/// delimited and wrapped in commas, so a key can be matched exactly with <c>LIKE '%,key,%'</c>
/// and never as a substring of another key. Ordering is the caller's job - the domain layer
/// normalizes the list before it gets here.
/// </summary>
public static class QualifierColumn
{
    private const char Delimiter = ',';

    public static string Serialize(IEnumerable<string>? keys)
    {
        var list = (keys ?? Enumerable.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToList();

        return list.Count == 0 ? string.Empty : $"{Delimiter}{string.Join(Delimiter, list)}{Delimiter}";
    }

    public static List<string> Parse(string? column) =>
        (column ?? string.Empty).Split(Delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>The <c>LIKE</c> pattern operand that matches rows carrying <paramref name="key"/>.</summary>
    public static string Token(string key) => $"{Delimiter}{key}{Delimiter}";
}
