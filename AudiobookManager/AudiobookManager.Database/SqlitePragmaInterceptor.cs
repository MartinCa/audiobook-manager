using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AudiobookManager.Database;

/// <summary>
/// Applies the per-connection SQLite settings this application needs, every time a connection is
/// opened.
///
/// Both settings are per-connection, which is what makes this an interceptor rather than a one-off
/// at startup: <c>journal_mode</c> is a property of the database file and persists, but
/// <c>synchronous</c> and <c>busy_timeout</c> reset to their defaults on every new connection, and
/// connection pooling means there are many.
///
/// Deliberately a separate interceptor from <c>AccentFoldingConnectionInterceptor</c>: that one
/// registers a scalar function before the connection opens, this one runs SQL after it has, and
/// keeping them apart means neither has to explain the other.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// How long a blocked writer waits for the lock before giving up. Microsoft.Data.Sqlite
    /// derives its own busy handling from the command timeout, but that is a per-command setting;
    /// stating it on the connection covers anything that does not go through a command with one.
    /// </summary>
    private const int BusyTimeoutMilliseconds = 30_000;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs after the connection is open, not before: unlike registering a function, these are SQL
    /// statements and need something to execute against.
    /// </summary>
    private static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        // Read, not set. EF Core creates the database in WAL, so that is the mode in production
        // and the mode `synchronous = NORMAL` below is safe under - and choosing the journal mode
        // is a heavier decision than this change is asking for. What is checked here is only
        // whether the assumption NORMAL depends on actually holds on this file.
        var journalMode = ExecuteScalarPragma(sqliteConnection, "PRAGMA journal_mode;");

        // Under WAL, NORMAL trades durability in a bounded way: a power loss can roll back
        // recently committed transactions, but cannot corrupt the file. Under a rollback journal
        // SQLite documents the same setting as carrying a small non-zero chance of *corruption* on
        // power loss - a different and unbounded trade. A database file this application did not
        // create is the case that comes apart: one restored from a backup tool or made with the
        // sqlite3 CLI arrives in `delete` mode, and EF leaves an existing file's mode alone. So
        // the setting is conditional on the mode rather than assumed alongside it.
        if (string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteNonQueryPragma(sqliteConnection, "PRAGMA synchronous = NORMAL;");
        }

        // Unconditional: unrelated to durability, and useful in any journal mode. Under a
        // rollback journal it matters more, not less - that is where writers block readers.
        ExecuteNonQueryPragma(sqliteConnection, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};");
    }

    private static string ExecuteScalarPragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private static void ExecuteNonQueryPragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
