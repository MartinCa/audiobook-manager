using System.Globalization;
using System.Text.RegularExpressions;

namespace AudiobookManager.Domain;

/// <summary>
/// The one canonical form of a series name and part, shared by the relation writer
/// (<c>SeriesRelationSync</c>), the refresh differ (<c>SeriesRelationSet</c>), the part
/// equivalence/sort functions, the incoming-DTO mapping and mirrored by the client twin
/// (<c>helpers/seriesRelations.ts</c>).
///
/// A name is trimmed, blank means "none", and two names are the same series when they match
/// ignoring case (the first spelling wins).
///
/// A part is one of three shapes: a plain number (<c>2</c>, <c>2.5</c>), a range of two numbers
/// (<c>1-3</c>, an omnibus), or - for anything else - free text kept as written (trimmed). Numbers
/// lose leading zeros and a trailing <c>.0</c>, a leading <c>Book</c>/<c>Vol</c>/<c>Volume</c>
/// label is dropped, and an en/em dash or <c>to</c> between two numbers becomes <c>-</c>. The
/// padded form that reaches the folder name and the tags (<c>Book 01-03</c>) is produced from this
/// at write time by <c>AudiobookTagHandler.PadSeriesPart</c>.
/// </summary>
public static partial class SeriesNormalization
{
    /// <summary>Case rule for "the same series": ordinal, ignoring case.</summary>
    public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>The name trimmed; null when it is null or blank.</summary>
    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex(@"^(?:vol(?:ume)?\.?|book)\s*(?=\d)", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPrefix();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(?:-|–|—|to)\s*(\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"^\d+(?:\.\d+)?$")]
    private static partial Regex NumberPattern();

    /// <summary>The canonical form of a part (see the class summary); null when blank.</summary>
    public static string? CleanPart(string? part)
    {
        var trimmed = CleanName(part);
        if (trimmed is null)
        {
            return null;
        }

        var body = LabelPrefix().Replace(trimmed, "").Trim();
        if (NumberPattern().IsMatch(body))
        {
            return CanonicalNumber(body);
        }

        var range = RangePattern().Match(body);
        if (range.Success)
        {
            var start = CanonicalNumber(range.Groups[1].Value);
            var end = CanonicalNumber(range.Groups[2].Value);
            // A range that runs backwards is not one; leave it exactly as the user wrote it.
            if (Compare(start, end) <= 0)
            {
                return $"{start}-{end}";
            }
        }

        return trimmed;
    }

    /// <summary>
    /// The number a part sorts and compares by: a plain number's own value, a range's lowest
    /// number, otherwise null. Parsed with the invariant culture.
    /// </summary>
    public static decimal? LeadingNumber(string? part)
    {
        var canonical = CleanPart(part);
        if (canonical is null)
        {
            return null;
        }

        if (canonical.Contains('-') && !IsRange(canonical))
        {
            return null;
        }

        var first = canonical.Split('-')[0];
        return NumberPattern().IsMatch(first)
            && decimal.TryParse(first, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Whether the (canonical) part is a range such as <c>1-3</c>.</summary>
    public static bool IsRange(string? part)
    {
        if (CleanPart(part) is not { } canonical)
        {
            return false;
        }

        var match = RangePattern().Match(canonical);
        return match.Success && Compare(match.Groups[1].Value, match.Groups[2].Value) <= 0;
    }

    // Digit strings can be longer than any numeric type, so this works on the text.
    private static string CanonicalNumber(string number)
    {
        var dot = number.IndexOf('.');
        var whole = (dot < 0 ? number : number[..dot]).TrimStart('0');
        var fraction = dot < 0 ? "" : number[(dot + 1)..].TrimEnd('0');
        if (whole.Length == 0)
        {
            whole = "0";
        }

        return fraction.Length == 0 ? whole : $"{whole}.{fraction}";
    }

    private static int Compare(string a, string b)
    {
        var aWhole = a.Split('.')[0];
        var bWhole = b.Split('.')[0];
        if (aWhole.Length != bWhole.Length)
        {
            return aWhole.Length.CompareTo(bWhole.Length);
        }

        var whole = string.CompareOrdinal(aWhole, bWhole);
        if (whole != 0)
        {
            return whole;
        }

        var aFraction = a.Contains('.') ? a[(a.IndexOf('.') + 1)..] : "";
        var bFraction = b.Contains('.') ? b[(b.IndexOf('.') + 1)..] : "";
        return string.CompareOrdinal(aFraction.PadRight(bFraction.Length, '0'), bFraction.PadRight(aFraction.Length, '0'));
    }
}
