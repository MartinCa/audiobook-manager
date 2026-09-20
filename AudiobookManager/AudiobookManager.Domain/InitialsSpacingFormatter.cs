using System.Text;

namespace AudiobookManager.Domain;

/// <summary>
/// Re-spaces and re-punctuates the run of single-letter initials in a person name to follow
/// <see cref="InitialsSpacing"/> and <see cref="InitialsPunctuation"/> preferences. The complement
/// of the client-side typeahead fold in <c>similarValueMatcher.ts</c> (which only collapses dotted-
/// initial spaces to make typing match stored names): this is the canonicalizer that defines what a
/// *stored* library name should look like, and what the initials-spacing consistency check
/// validates against.
///
/// The two settings are independent: <see cref="InitialsSpacing"/> governs the whitespace BETWEEN
/// adjacent initials, <see cref="InitialsPunctuation"/> governs whether each initial carries a
/// trailing period. The space between the last initial and the following word is always a single
/// space, whatever either setting is: "J.R.Tolkien" and "J R Tolkien" (no space before the
/// surname) are never canonical forms.
/// </summary>
public static class InitialsSpacingFormatter
{
    /// <summary>
    /// Formats <paramref name="name"/> to the canonical form under <paramref name="spacing"/> and
    /// <paramref name="punctuation"/>. A name with no initials round-trips unchanged.
    /// </summary>
    public static string Format(string name, InitialsSpacing spacing, InitialsPunctuation punctuation)
    {
        // Runs of adjacent single-letter initials, e.g. ["J", "K"] from "J. K. Rowling", "J K
        // Rowling", or parsed out of a single concatenated token like "J.K." or "JK".
        var initialsRun = new List<string>();
        var result = new StringBuilder();
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (IsInitialToken(token))
            {
                initialsRun.AddRange(SplitInitialToken(token));
            }
            else
            {
                FlushInitialsRun(result, initialsRun, spacing, punctuation);
                if (result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(token);
            }
        }

        FlushInitialsRun(result, initialsRun, spacing, punctuation);
        return result.ToString();
    }

    /// <summary>
    /// True when <paramref name="name"/> already follows <paramref name="spacing"/> and
    /// <paramref name="punctuation"/>.
    /// </summary>
    public static bool IsCompliant(string name, InitialsSpacing spacing, InitialsPunctuation punctuation) =>
        string.Equals(Format(name, spacing, punctuation), name, StringComparison.Ordinal);

    /// <summary>
    /// An initial token is either a chain of single letters each followed by a period, with no
    /// spaces ("J.", "K.", "J.K.", "J.R.R."), or a bare chain of single uppercase letters with no
    /// periods ("J", "JK", "JRR"). Multi-letter dotless words ("Rowling", "St.", "Jr.") are not
    /// initials: "St." is S-t-dot (two letters before the dot) and fails the dotted single-letter
    /// rule, and "Rowling" fails the undotted all-uppercase rule.
    ///
    /// The undotted rule is a heuristic shared with the rest of this canonicalizer: an all-
    /// uppercase word that happens to be a real surname or initialism (e.g. "NG") is
    /// indistinguishable from a run of undotted initials without more context, and is treated as
    /// initials here, same as the existing tradeoff for the dotted form.
    /// </summary>
    private static bool IsInitialToken(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        if (token[^1] == '.')
        {
            return token.Length >= 2
                && token
                    .Chunk(2)
                    .All(pair => pair.Length == 2 && char.IsLetter(pair[0]) && pair[1] == '.');
        }

        return token.All(char.IsUpper);
    }

    /// <summary>
    /// "J.K." -> ["J", "K"]; "JK" -> ["J", "K"]. The bare letters, with any period stripped -
    /// <see cref="FlushInitialsRun"/> re-applies punctuation per the configured setting. Only
    /// called on tokens <see cref="IsInitialToken"/> accepts.
    /// </summary>
    private static IEnumerable<string> SplitInitialToken(string token)
    {
        if (token[^1] == '.')
        {
            for (var i = 0; i < token.Length; i += 2)
            {
                yield return token[i].ToString();
            }
        }
        else
        {
            foreach (var c in token)
            {
                yield return c.ToString();
            }
        }
    }

    /// <summary>
    /// Appends the accumulated initials, each punctuated per <paramref name="punctuation"/> and
    /// joined by either nothing or a single space per <paramref name="spacing"/>.
    /// </summary>
    private static void FlushInitialsRun(
        StringBuilder result, List<string> run, InitialsSpacing spacing, InitialsPunctuation punctuation)
    {
        if (run.Count == 0)
        {
            return;
        }

        if (result.Length > 0)
        {
            result.Append(' ');
        }

        var punctuated = run.Select(letter =>
            punctuation == InitialsPunctuation.Dotted ? letter + "." : letter);
        result.Append(string.Join(spacing == InitialsSpacing.Spaced ? " " : "", punctuated));
        run.Clear();
    }
}
