using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Search;

/// <summary>
/// SQLite's default BINARY collation (used by LIKE/Contains everywhere in this codebase - there
/// is no ICU/custom collation registered) compares code points, so it never folds diacritics:
/// "Rene" does not match "René". This registers a SQLite scalar function that strips combining
/// marks, so search predicates can wrap both sides in it to become accent-insensitive.
/// </summary>
public static class AccentFolding
{
    public const string SqlFunctionName = "fold_accents";

    /// <summary>
    /// Marker for use inside LINQ query expressions - EF Core translates calls to this specific
    /// method into a call to the <see cref="SqlFunctionName"/> SQL function (registered via
    /// <see cref="Register"/>) rather than invoking this body, which is never reached at runtime.
    /// </summary>
    [DbFunction(SqlFunctionName)]
    public static string? Fold(string? value) =>
        throw new NotSupportedException($"{nameof(Fold)} can only be used inside a LINQ query expression.");

    /// <summary>
    /// The actual CLR implementation, used both as the registered SQL function body and to fold
    /// a user-typed search term into a pattern in application code before it reaches SQL.
    /// </summary>
    public static string? FoldPlain(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static void Register(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>(SqlFunctionName, FoldPlain);
    }
}
