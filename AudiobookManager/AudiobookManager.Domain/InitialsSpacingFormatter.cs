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
///
/// Deliberately conservative about recognizing an UNDOTTED initial in already-stored text: a dot
/// is a strong, unambiguous signal ("J." can only be an initial), but a bare uppercase token is
/// not - "III" (a Roman-numeral suffix), "JOHN"/"SMITH" (an all-caps stored name) and a sentence-
/// initial "A"/"I" are all indistinguishable from genuine undotted initials by spelling alone. An
/// earlier version of this canonicalizer treated *any* all-uppercase token as initials and
/// rewrote "John Smith III" to "John Smith I.I.I." and "JOHN SMITH" to one fused initials run -
/// see <see cref="IsBareSingleLetterToken"/> for the fix. The tradeoff is a real one: a name
/// already stored as bare undotted initials with no neighboring initial ("H Rider Haggard", or a
/// concatenated run like "JRR Tolkien") is not detected as such and is left alone rather than
/// reformatted - a missed rewrite is an acceptable cost, a corrupted name is not.
/// </summary>
public static class InitialsSpacingFormatter
{
    /// <summary>
    /// Formats <paramref name="name"/> to the canonical form under <paramref name="spacing"/> and
    /// <paramref name="punctuation"/>. A name with no initials round-trips unchanged.
    /// </summary>
    public static string Format(string name, InitialsSpacing spacing, InitialsPunctuation punctuation)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isInitial = ClassifyInitialTokens(tokens);

        // Runs of adjacent single-letter initials, e.g. ["J", "K"] from "J. K. Rowling" or "J K
        // Rowling", or parsed out of a single dotted token like "J.K.".
        var initialsRun = new List<string>();
        var result = new StringBuilder();

        for (var i = 0; i < tokens.Length; i++)
        {
            if (isInitial[i])
            {
                initialsRun.AddRange(SplitInitialToken(tokens[i]));
            }
            else
            {
                FlushInitialsRun(result, initialsRun, spacing, punctuation);
                if (result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(tokens[i]);
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
    /// Decides, per token, whether it is part of an initials run. A dotted token ("J.", "J.K.")
    /// is always certain - the dot is unambiguous. A bare single uppercase letter ("J") is only
    /// promoted to an initial when it sits in a maximal run of consecutive dotted-or-bare-single
    /// tokens that either contains a dotted token or has at least two members - i.e. a lone bare
    /// letter next to ordinary words ("A Tale of Two Cities") is left as a word, but "J K Rowling"
    /// (a run of two bare letters) and "H. Rider Haggard" (a run of one dotted letter) both
    /// qualify. A multi-letter bare token ("JOHN", "III", "JRR") is never treated as concatenated
    /// initials - see the class doc comment for why that used to corrupt real names.
    /// </summary>
    private static bool[] ClassifyInitialTokens(string[] tokens)
    {
        var isDotted = new bool[tokens.Length];
        var isBareSingle = new bool[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            isDotted[i] = IsDottedInitialToken(tokens[i]);
            isBareSingle[i] = !isDotted[i] && IsBareSingleLetterToken(tokens[i]);
        }

        var isInitial = new bool[tokens.Length];
        var start = 0;
        while (start < tokens.Length)
        {
            if (!isDotted[start] && !isBareSingle[start])
            {
                start++;
                continue;
            }

            var end = start;
            var containsDotted = false;
            while (end < tokens.Length && (isDotted[end] || isBareSingle[end]))
            {
                containsDotted |= isDotted[end];
                end++;
            }

            if (containsDotted || end - start >= 2)
            {
                for (var k = start; k < end; k++)
                {
                    isInitial[k] = true;
                }
            }

            start = end;
        }

        return isInitial;
    }

    /// <summary>
    /// A chain of single letters each followed by a period, with no spaces: "J.", "J.K.", "J.R.R.".
    /// Multi-letter dotless words ("Rowling", "St.", "Jr.") are not initials: "St." is S-t-dot (two
    /// letters before the dot) and fails the single-letter rule.
    /// </summary>
    private static bool IsDottedInitialToken(string token) =>
        token.Length >= 2
        && token[^1] == '.'
        && token
            .Chunk(2)
            .All(pair => pair.Length == 2 && char.IsLetter(pair[0]) && pair[1] == '.');

    /// <summary>A single bare uppercase letter with no period: "J".</summary>
    private static bool IsBareSingleLetterToken(string token) =>
        token.Length == 1 && char.IsUpper(token[0]);

    /// <summary>
    /// "J.K." -> ["J", "K"]; "J" -> ["J"]. The bare letters, with any period stripped -
    /// <see cref="FlushInitialsRun"/> re-applies punctuation per the configured setting. Only
    /// called on tokens <see cref="ClassifyInitialTokens"/> marked as initials.
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
            yield return token;
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
