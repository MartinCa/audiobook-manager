using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AudiobookManager.Database;

/// <summary>
/// Applies the per-connection SQLite settings this application needs, every time a connection is
/// opened.
///
/// The default journal mode is the reason this exists. In rollback-journal mode a writer takes an
/// exclusive lock on the whole database and blocks every reader for the duration of its
/// transaction - and this application is built to have several at once: the OrganizeWorker
/// processes its queue while a library scan or consistency check runs on the thread pool and the
/// user saves a book from the editor. The unique-violation recovery in
/// <c>PersonRepository.GetOrCreatePersons</c> and <c>GenreRepository.GetOrCreateGenres</c> exists
/// precisely because those really do overlap. Under contention every affected request waited out
/// the full command timeout before surfacing "database is locked".
///
/// WAL lets readers continue while a writer works, which is the shape this workload actually has.
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
    /// statements and need something to execute against. They also have to run before any
    /// transaction starts - SQLite refuses to change the journal mode inside one - which opening is
    /// the natural point for.
    /// </summary>
    private static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        // journal_mode is a property of the database file and survives, so this is a no-op after
        // the first connection; synchronous and busy_timeout are per-connection and are not.
        //
        // Not asserted afterwards, deliberately. WAL needs shared memory, so it is unavailable on
        // some file systems a container might hold the database on - a network mount being the
        // case that matters here - and SQLite answers by staying in the previous journal mode
        // rather than failing. Correctness is unaffected either way; only contention is, so
        // refusing to start over it would be worse than running in the old mode. Anyone
        // investigating lock contention can read the mode back with `PRAGMA journal_mode`.
        using var command = sqliteConnection.CreateCommand();
        command.CommandText =
            $"""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = {BusyTimeoutMilliseconds};
            """;
        command.ExecuteNonQuery();
    }
}
