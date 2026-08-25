using System.Text;
using System.Text.RegularExpressions;

namespace AudiobookManager.Services.Similarity;

/// <summary>
/// Normalizes free-text names/series values for comparison-only fuzzy matching.
/// The normalized output is never written back to the database - it exists purely
/// to compare two raw strings for near-equality.
/// </summary>
public static class NameNormalizer
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a raw string for fuzzy comparison:
    /// - lowercases and trims
    /// - replaces "&amp;" with "and"
    /// - strips periods
    /// - collapses whitespace
    /// - merges single-letter initial tokens (e.g. "j k rowling" -> "jk rowling")
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.Replace("&", " and ");
        normalized = normalized.Replace(".", " ");
        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var merged = new List<string>();
        var initialsBuffer = new StringBuilder();

        foreach (var token in tokens)
        {
            if (token.Length == 1)
            {
                initialsBuffer.Append(token);
            }
            else
            {
                if (initialsBuffer.Length > 0)
                {
                    merged.Add(initialsBuffer.ToString());
                    initialsBuffer.Clear();
                }
                merged.Add(token);
            }
        }

        if (initialsBuffer.Length > 0)
        {
            merged.Add(initialsBuffer.ToString());
        }

        return string.Join(" ", merged);
    }
}
