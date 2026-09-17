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
/// every metadata search from then on. <see cref="MatchTimeout"/> is what bounds that, and it is
/// applied by construction here so a call site cannot forget it.</item>
/// </list>
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
    /// Compiles a user-authored pattern with <see cref="MatchTimeout"/> applied. Throws
    /// <see cref="ArgumentException"/> for a pattern that does not compile, exactly as
    /// <see cref="Regex"/> does.
    /// </summary>
    public static Regex Compile(string pattern) => new(pattern, RegexOptions.None, MatchTimeout);

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
