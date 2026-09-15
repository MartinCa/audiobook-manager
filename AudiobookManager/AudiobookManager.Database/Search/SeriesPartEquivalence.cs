using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Search;

/// <summary>
/// The canonical series-part equivalence (the "existing series-part equivalence rules" the
/// advisory conflict check and the series reconciliation both use): two parts are the same when
/// both parse as numbers and are within rounding of each other ("1", "1.0", "2.5"), or otherwise
/// when their trimmed texts compare case-insensitively ("Book 1" vs "book 1"). A blank part is
/// never equivalent to any part.
///
/// This is a Database-layer helper on purpose: it is registered as a SQLite scalar function so a
/// query (the series-part conflict read) can apply the equivalence in SQL rather than fetching a
/// bounded candidate row first and filtering in memory - which can silently miss a conflict that
/// sorts past the bound. The service layer (SeriesService) delegates to the same CLR
/// implementation so the reconciliation and the conflict check cannot drift.
/// </summary>
public static class SeriesPartEquivalence
{
    public const string SqlFunctionName = "parts_equivalent";

    /// <summary>
    /// Marker for use inside LINQ query expressions - EF Core translates calls to this method into
    /// a call to the <see cref="SqlFunctionName"/> SQL function (registered via
    /// <see cref="Register"/>) rather than invoking this body, which is never reached at runtime.
    /// </summary>
    [DbFunction(SqlFunctionName)]
    public static bool PartsEquivalent(string? a, string? b) =>
        throw new NotSupportedException(
            $"{nameof(PartsEquivalent)} can only be used inside a LINQ query expression.");

    /// <summary>
    /// The real CLR implementation, used both as the registered SQL function body and by
    /// <see cref="SeriesService"/> for the reconciliation's in-memory comparisons. A blank part
    /// is never equivalent to any part - including another blank: an empty series part means
    /// "this book has no position", which is its own informational state in the edit form, not
    /// something two books can share.
    /// </summary>
    public static bool PartsEquivalentClr(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var numA) &&
            double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var numB))
        {
            return Math.Abs(numA - numB) < 0.0001;
        }

        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, bool>(SqlFunctionName, PartsEquivalentClr);
    }
}