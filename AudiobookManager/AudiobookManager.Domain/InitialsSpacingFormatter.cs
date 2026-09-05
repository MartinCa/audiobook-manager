using System.Text;

namespace AudiobookManager.Domain;

/// <summary>
/// Re-spaces the run of dotted single-letter initials in a person name to follow a
/// <see cref="InitialsSpacing"/> preference. The complement of the client-side typeahead fold in
/// <c>similarValueMatcher.ts</c> (which only collapses dotted-initial spaces to make typing match
/// stored names): this is the canonicalizer that defines what a *stored* library name should look
/// like, and what the initials-spacing consistency check validates against.
///
/// Only whitespace BETWEEN two adjacent dotted initials is governed ("J. K. Rowling" vs
/// "J.K. Rowling"). The space between the last initial and the following word is always a single
/// space, whatever the setting is: "J.K.Rowling" is never the canonical form.
/// </summary>
public static class InitialsSpacingFormatter
{
    /// <summary>
    /// Formats <paramref name="name"/> to the canonical form under <paramref name="spacing"/>.
    /// A name with no dotted initials (or only one) round-trips unchanged.
    /// </summary>
    public static string Format(string name, InitialsSpacing spacing)
    {
        // Runs of adjacent dotted single-letter initials, e.g. ["J.", "K."] from "J. K. Rowling"
        // or ["J.", "K."] parsed out of the single token "J.K.".
        var initialsRun = new List<string>();
        var result = new StringBuilder();
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (IsInitialToken(token))
            {
                // Split a concatenated token ("J.K.") into its single initials; a token that is
                // already a single initial ("J.") is its own split result.
                initialsRun.AddRange(SplitInitialToken(token));
            }
            else
            {
                FlushInitialsRun(result, initialsRun, spacing);
                if (result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(token);
            }
        }

        FlushInitialsRun(result, initialsRun, spacing);
        return result.ToString();
    }

    /// <summary>True when <paramref name="name"/> already follows <paramref name="spacing"/>.</summary>
    public static bool IsCompliant(string name, InitialsSpacing spacing) =>
        string.Equals(Format(name, spacing), name, StringComparison.Ordinal);

    /// <summary>
    /// An initial token is a chain of single letters each followed by a period, with no spaces:
    /// "J.", "K.", "J.K.", "J.R.R.". Multi-letter dotless words ("Rowling", "St.", "Jr.") are not
    /// initials: "St." is S-t-dot (two letters before the dot) and fails the single-letter rule.
    /// </summary>
    private static bool IsInitialToken(string token) =>
        token.Length >= 2
        && token[^1] == '.'
        && token
            .Chunk(2)
            .All(pair => pair.Length == 2 && char.IsLetter(pair[0]) && pair[1] == '.');

    /// <summary>"J.K." -> ["J.", "K."]. Only called on tokens <see cref="IsInitialToken"/> accepts.</summary>
    private static IEnumerable<string> SplitInitialToken(string token)
    {
        for (var i = 0; i < token.Length; i += 2)
        {
            yield return token.Substring(i, 2);
        }
    }

    /// <summary>Appends the accumulated initials joined by either nothing or a single space.</summary>
    private static void FlushInitialsRun(StringBuilder result, List<string> run, InitialsSpacing spacing)
    {
        if (run.Count == 0)
        {
            return;
        }

        if (result.Length > 0)
        {
            result.Append(' ');
        }

        result.Append(string.Join(spacing == InitialsSpacing.Spaced ? " " : "", run));
        run.Clear();
    }
}