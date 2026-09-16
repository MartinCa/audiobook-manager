using System.Globalization;

namespace AudiobookManager.Services.Similarity;

/// <summary>
/// The single CLR-side definition of "are two series-part values the same part", shared by the
/// roster matcher (<see cref="SeriesRosterMatcher"/>) and through it every consumer that compares
/// a stored <c>SeriesPart</c> against a roster position - the reconciliation's part-mismatch pass
/// and the series-refresh diff both route through <see cref="SeriesRosterMatcher.PartsEquivalent"/>.
///
/// The equality is deliberately loose: parts are free text ("2", "2.0", "2.5", "Book 2"), so two
/// values that both parse as the same number are the same part, and otherwise they compare as
/// trimmed, case-insensitive text. "2" and "2.0" are the same part; "2" and "7" are not; "Book 2"
/// is only itself.
/// </summary>
public static class SeriesPartEquivalence
{
    /// <summary>
    /// Whether two part values denote the same part. The numeric tolerance mirrors
    /// <see cref="PositionsEqual"/>'s historical epsilon: it exists to paper over float parsing
    /// wobble, not to fold genuinely different numbers together.
    /// </summary>
    public static bool PartsEquivalentClr(string? a, string? b)
    {
        if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var numA) &&
            double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var numB))
        {
            return Math.Abs(numA - numB) < 0.0001;
        }

        return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}