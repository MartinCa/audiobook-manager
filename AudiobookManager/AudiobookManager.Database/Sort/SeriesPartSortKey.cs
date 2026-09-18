using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Sort;

/// <summary>
/// Orders a series-part column the way a reader does, in SQL: a numeric part sorts by its value -
/// "2" before "17.5" - while SQLite's BINARY collation would put them in code-point order
/// ("17.5" before "2"). A non-numeric part ("Book 2") sorts after every numeric one, and a blank
/// part (null, empty, whitespace-only) sorts last.
///
/// <see cref="KeyPlain"/> is the single definition of that tier order, shared rather than mirrored:
/// <c>SeriesRosterMatcher.PositionSortKey</c>, which orders the expected-books roster in memory,
/// calls it for the numeric component of its own key. It used to restate the tiers instead and got
/// the blank tier wrong (see <see cref="BlankTier"/>), so the two are deliberately one
/// implementation now. The remaining difference is only what each caller adds on top: the roster
/// sort appends the part's text so its non-numeric parts order alphabetically, while here every
/// non-numeric part ties and the caller's then-by columns (BookName, Id) break it.
///
/// This has to live in a SQLite scalar function rather than borrowing the service's in-memory
/// sort, because the series detail's owned list is paged in SQL and a paged query needs its total
/// order in SQL (see AGENTS.md): sorting the survivors in memory and re-Skip/Taking them would
/// order the wrong rows. The function is registered per-connection by
/// <see cref="SeriesPartSortKeyConnectionInterceptor"/>, exactly like <see cref="AccentFolding"/>.
/// </summary>
public static class SeriesPartSortKey
{
    public const string SqlFunctionName = "series_part_sort_key";

    /// <summary>
    /// Marker for use inside LINQ query expressions - EF Core translates calls to this specific
    /// method into a call to the <see cref="SqlFunctionName"/> SQL function (registered via
    /// <see cref="Register"/>) rather than invoking this body, which is never reached at runtime.
    /// </summary>
    [DbFunction(SqlFunctionName)]
    public static double Key(string? value) =>
        throw new NotSupportedException($"{nameof(Key)} can only be used inside a LINQ query expression.");

    // NumberStyles is deliberately narrowed from the service's NumberStyles.Any: it drops
    // AllowParentheses / AllowTrailingSign / AllowCurrencySymbol formats a part number should not
    // have. It cannot also exclude "NaN" / "Infinity" / "-Infinity" - InvariantCulture recognizes
    // those tokens under any NumberStyles - so the double.IsFinite guard is what actually demotes
    // them (along with "1e309", which Float's AllowExponent overflows to +Infinity).
    private const NumberStyles NumericStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    /// <summary>
    /// The key every non-numeric non-blank part collapses to, after every finite numeric one.
    /// Written as <c>Double.MaxValue</c> rather than the <c>Double.MaxValue - 1</c> it reads like
    /// in prose: the subtraction rounds back to <c>Double.MaxValue</c> at that magnitude (the ULP
    /// there is ~2^971), so the two spellings are the same double and the honest one is this.
    /// </summary>
    public const double NonNumericTier = double.MaxValue;

    /// <summary>
    /// The key a blank part sorts on, after <see cref="NonNumericTier"/>. It has to be
    /// <c>Double.PositiveInfinity</c> and not <c>Double.MaxValue</c>: the two tiers would
    /// otherwise be the same double and "blank parts last" would silently fall to whatever
    /// tiebreaker the caller applies next.
    /// </summary>
    public const double BlankTier = double.PositiveInfinity;

    /// <summary>
    /// The actual CLR implementation, used as the registered SQL function body and as the numeric
    /// component of <c>SeriesRosterMatcher.PositionSortKey</c>'s in-memory key. Numeric parts parse
    /// with <c>CultureInfo.InvariantCulture</c> and only finite results are treated as numeric:
    ///
    /// - a finite numeric part returns its value, so "2" sorts before "17.5". Negative parts
    ///   ("-2.5") keep their value and simply sort before the positive ones;
    /// - a non-numeric non-blank part returns <see cref="NonNumericTier"/>, after every finite
    ///   numeric part. They all tie - that is fine, they only need to come after the numerics,
    ///   not to be ordered among themselves; whatever the caller sorts by next breaks the tie;
    /// - a blank part returns <see cref="BlankTier"/>, which SQLite sorts after every finite real.
    /// </summary>
    public static double KeyPlain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BlankTier;
        }

        if (double.TryParse(value, NumericStyles, CultureInfo.InvariantCulture, out var numeric)
            && double.IsFinite(numeric))
        {
            return numeric;
        }

        return NonNumericTier;
    }

    public static void Register(SqliteConnection connection)
    {
        connection.CreateFunction<string?, double>(SqlFunctionName, KeyPlain);
    }
}