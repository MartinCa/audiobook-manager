using System.Text.RegularExpressions;

namespace AudiobookManager.Scraping;

/// <summary>
/// The single place a user-authored series-mapping pattern becomes a <see cref="Regex"/>.
///
/// These patterns are free text a user types into the series detail's mapping dialog, and every
/// scraped metadata result is matched against every one of them, so they are the one regex in this
/// codebase whose text the application does not control. That makes two failure modes real rather
/// than theoretical, and neither may be allowed to take a metadata search down:
///
/// <list type="bullet">
/// <item>A pattern that does not compile. <see cref="TryCompile"/> reports it so the write
/// endpoints can refuse it with a 4xx the user can act on, instead of accepting a row that is
/// silently skipped at load time with nothing but a log line to explain why the mapping never
/// fires.</item>
/// <item>A pattern that compiles but backtracks catastrophically - the classic <c>(a+)+$</c>
/// shape. With .NET's default <see cref="Regex.InfiniteMatchTimeout"/> a single such pattern
/// wedges the request thread forever, and because these run on every scraped result it wedges
/// every metadata search from then on.</item>
/// </list>
///
/// The second is prevented rather than merely bounded wherever that is possible.
/// <see cref="Compile"/> builds the pattern on the <see cref="RegexOptions.NonBacktracking"/>
/// engine first, which matches in guaranteed linear time and so cannot backtrack at all - a
/// pattern on that engine is immune by construction and its timeout is unreachable. Only the
/// constructs that engine does not implement fall back to the classic one, and those keep
/// <see cref="MatchTimeout"/> as their bound.
///
/// That distinction earns its keep because the timeout is per match while these patterns are
/// matched once per scraped result: N runaway patterns would otherwise cost N x the timeout for
/// every result in a search. NonBacktracking takes almost all of them out of that multiplication,
/// and the caller closes the rest by disabling a pattern that does time out for the remainder of
/// the scope (see <c>BookSeriesMapper</c>), so a bad row costs its timeout once per request
/// rather than once per result.
///
/// A timeout only fires on a match that is already pathological, so callers treat it as "this
/// mapping does not apply" and carry on with the rest - never as a failed search.
/// </summary>
public static class SeriesMappingPattern
{
    /// <summary>
    /// The per-match ceiling. A series name is short and these patterns are simple, so a healthy
    /// match finishes in microseconds; this is sized to be unreachable by anything except genuine
    /// catastrophic backtracking, not to be a performance budget.
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Compiles a user-authored pattern on the strongest engine that supports it: the linear-time
    /// <see cref="RegexOptions.NonBacktracking"/> one where possible, the classic engine bounded by
    /// <see cref="MatchTimeout"/> otherwise. Throws <see cref="ArgumentException"/> for a pattern
    /// that does not compile at all, exactly as <see cref="Regex"/> does - so a pattern this
    /// accepts is one the classic engine accepts, and the engine choice never changes which
    /// patterns are valid, only how fast the pathological ones give up.
    ///
    /// The timeout is passed on both paths. It is unreachable on the NonBacktracking one, and
    /// saying so in code costs nothing next to relying on the reader knowing it.
    /// </summary>
    public static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.NonBacktracking, MatchTimeout);
        }
        catch (NotSupportedException)
        {
            // A construct the linear-time engine does not implement - a backreference, a
            // lookaround, an atomic group. Perfectly valid regex, so it falls back to the classic
            // engine, where the timeout stops being a formality.
            return new Regex(pattern, RegexOptions.None, MatchTimeout);
        }
    }

    /// <summary>
    /// Whether this compiled pattern can backtrack at all, i.e. whether its
    /// <see cref="MatchTimeout"/> is load-bearing rather than unreachable.
    /// </summary>
    public static bool CanBacktrack(Regex regex) => (regex.Options & RegexOptions.NonBacktracking) == 0;

    /// <summary>
    /// Compiles a user-authored pattern, reporting a syntax error rather than throwing.
    /// <paramref name="error"/> carries the framework's own message, which names the position and
    /// the construct at fault and is safe to relay to the caller who typed the pattern.
    /// </summary>
    public static bool TryCompile(string pattern, out Regex? regex, out string? error)
    {
        try
        {
            regex = Compile(pattern);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            regex = null;
            error = ex.Message;
            return false;
        }
    }
}
