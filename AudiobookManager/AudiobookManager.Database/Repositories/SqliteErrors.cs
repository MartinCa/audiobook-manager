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

    /// <summary>
    /// Whether the failure was SQLITE_BUSY (5) or SQLITE_LOCKED (6) - the "another connection has
    /// the database (or a row) locked" codes. The extended code is deliberately not consulted: a
    /// busy failure can carry any of several extended variants (SQLITE_BUSY_SNAPSHOT, ...), and
    /// they all mean the same thing here - "try again".
    ///
    /// Walks the <see cref="Exception.InnerException"/> chain because EF wraps store failures
    /// raised inside <c>SaveChanges</c>/<c>SaveChangesAsync</c> in a <see cref="DbUpdateException"/>.
    /// </summary>
    public static bool IsBusyLocked(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6)
            {
                return true;
            }
        }

        return false;
    }
}
