using System.Text.RegularExpressions;

namespace AudiobookManager.Domain;

/// <summary>
/// One user-configured rule: within a title scraped from <see cref="Source"/>, a bracketed
/// <see cref="Indicator"/> (<c>[Dramatized Adaptation]</c>, <c>(Abridged)</c>) means the book
/// carries the qualifier <see cref="QualifierKey"/>.
/// </summary>
public sealed record QualifierIndicatorRule(string Source, string Indicator, string QualifierKey);

/// <summary>
/// Turns the ways online sources spell a qualifier into the qualifier itself, so the stored book
/// name stays clean (see <see cref="BookQualifiers"/>). Sources do not agree on wording - Audible
/// has <c>[Dramatized Adaptation]</c>, <c>(Full-Cast Dramatized Adaptation)</c> and plain
/// <c>(Dramatized)</c> - so the mapping from wording to qualifier is configurable per source rather
/// than a fixed list.
///
/// An indicator only counts when it is the whole content of a <c>(...)</c> or <c>[...]</c> group,
/// compared case-insensitively. That keeps a title that merely contains the word ("A Dramatized
/// History of Rome") and a group with other content ("(Part 1 of 2)") untouched, and lets two
/// groups sit in one title: <c>House of Earth and Blood (Part 1 of 2) (Dramatized Adaptation)</c>
/// loses only the second.
/// </summary>
public static partial class QualifierIndicators
{
    [GeneratedRegex(@"\s*[\(\[]([^\(\)\[\]]*)[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketGroup();

    /// <summary>
    /// The indicator as it is compared and stored: trimmed, inner whitespace collapsed, and a
    /// surrounding <c>()</c>/<c>[]</c> dropped, so <c>[Dramatized Adaptation]</c> and
    /// <c>Dramatized Adaptation</c> are the same rule. Empty when nothing is left.
    /// </summary>
    public static string NormalizeIndicator(string? indicator)
    {
        var text = (indicator ?? string.Empty).Trim();
        if (text.Length >= 2
            && ((text[0] == '(' && text[^1] == ')') || (text[0] == '[' && text[^1] == ']')))
        {
            text = text[1..^1];
        }

        return Collapse(text);
    }

    /// <summary>
    /// Removes every rule-matched bracket group from <paramref name="title"/> and returns the
    /// cleaned title with the canonical keys found. A title that is nothing but an indicator is
    /// returned unchanged - a book must keep a name. Rules for other sources are ignored.
    /// </summary>
    public static (string? Title, List<string> Qualifiers) Extract(
        string? title, string? source, IEnumerable<QualifierIndicatorRule>? rules)
    {
        if (string.IsNullOrWhiteSpace(title) || rules is null)
        {
            return (title, new List<string>());
        }

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (!string.Equals(rule.Source, source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var indicator = NormalizeIndicator(rule.Indicator);
            if (indicator.Length > 0)
            {
                lookup.TryAdd(indicator, rule.QualifierKey);
            }
        }

        if (lookup.Count == 0)
        {
            return (title, new List<string>());
        }

        var found = new List<string>();
        var cleaned = BracketGroup().Replace(title, match =>
        {
            if (lookup.TryGetValue(Collapse(match.Groups[1].Value), out var key))
            {
                found.Add(key);
                return string.Empty;
            }

            return match.Value;
        }).Trim();

        if (found.Count == 0 || cleaned.Length == 0)
        {
            return (title, new List<string>());
        }

        return (cleaned, BookQualifiers.Normalize(found));
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
