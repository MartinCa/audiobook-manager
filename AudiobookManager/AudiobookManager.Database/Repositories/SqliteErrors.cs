using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Recognises the SQLite failures a repository is expected to recover from rather than surface.
/// </summary>
internal static class SqliteErrors
{
    /// <summary>
    /// Whether the save failed on a uniqueness constraint - SQLITE_CONSTRAINT_UNIQUE (2067) or
    /// SQLITE_CONSTRAINT_PRIMARYKEY (1555).
    ///
    /// Every "read, then insert if absent" in this layer spans an await on a request-scoped
    /// context, so two requests can both see a row as missing and both insert it. That is a real
    /// race here, not a theoretical one: organizes run concurrently with interactive saves and
    /// with the bulk operations. The loser has to adopt the winner's row instead of failing the
    /// whole operation.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException sqlite &&
        (sqlite.SqliteExtendedErrorCode == 2067 || sqlite.SqliteExtendedErrorCode == 1555);
}
