using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Sort;

/// <summary>
/// Orders a series-part column the way a reader does, in SQL: a numeric part sorts by its value -
/// "2" before "17.5" - while SQLite's BINARY collation would put them in code-point order
/// ("17.5" before "2"). A non-numeric part ("Book 2") sorts after every numeric one, and a blank
/// part (null, empty, whitespace-only) sorts last. The tier order (numeric by value, non-numeric
/// after, blank last) mirrors <c>SeriesService.PositionSortKey</c>, which orders the expected-books
/// roster the same way - but it is not full parity: here every non-numeric part collapses to one
/// key and the caller's then-by columns (BookName, Id) order them, whereas <c>PositionSortKey</c>
/// also sorts non-numeric parts by their text.
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
    /// The actual CLR implementation, used as the registered SQL function body. It shares
    /// <c>SeriesService.PositionSortKey</c>'s tier order but is deliberately not a full mirror:
    /// here every non-numeric part collapses to a single key and the caller's then-by columns
    /// (BookName, Id) order them, whereas <c>PositionSortKey</c> sorts the roster's non-numeric
    /// positions by their text. Numeric parts parse with <c>CultureInfo.InvariantCulture</c> (as
    /// the service does) and only finite results are treated as numeric:
    ///
    /// - a finite numeric part returns its value, so "2" sorts before "17.5". Negative parts
    ///   ("-2.5") keep their value and simply sort before the positive ones;
    /// - a non-numeric non-blank part returns <c>Double.MaxValue - 1</c>, after every finite
    ///   numeric part. At that magnitude the subtraction rounds back to <c>Double.MaxValue</c>
    ///   in the CLR's own double arithmetic, so non-numeric parts all tie - that is fine, they
    ///   only need to come after the numerics, not to be ordered among themselves; the then-by
    ///   columns break their tie;
    /// - a blank part returns <c>Double.PositiveInfinity</c>, which SQLite sorts after every
    ///   finite real. Plain <c>Double.MaxValue</c> would not keep the blank tier last: because
    ///   <c>Double.MaxValue - 1</c> equals <c>Double.MaxValue</c> as a double, the blank tier
    ///   would tie with the non-numeric tier and "blank parts last" would silently fall to the
    ///   then-by columns.
    /// </summary>
    public static double KeyPlain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return double.PositiveInfinity;
        }

        if (double.TryParse(value, NumericStyles, CultureInfo.InvariantCulture, out var numeric)
            && double.IsFinite(numeric))
        {
            return numeric;
        }

        return double.MaxValue - 1;
    }

    public static void Register(SqliteConnection connection)
    {
        connection.CreateFunction<string?, double>(SqlFunctionName, KeyPlain);
    }
}