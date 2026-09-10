using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AudiobookManager.Database.Sort;

/// <summary>
/// Registers the <see cref="SeriesPartSortKey"/> SQLite scalar function on every connection.
/// Registering a custom function can only happen once EF Core has actually created the ADO.NET
/// connection, which is not yet true inside <c>DbContext.OnConfiguring</c> - the context is still
/// being configured at that point, and touching <c>Database.GetDbConnection()</c> there throws. A
/// connection interceptor is the supported hook: it fires right as a (possibly pooled/reused)
/// connection is about to open, so the function is always registered before any query can run
/// against it - the same reason <see cref="AccentFoldingConnectionInterceptor"/> exists as its own
/// interceptor.
/// </summary>
public sealed class SeriesPartSortKeyConnectionInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        SeriesPartSortKey.Register((SqliteConnection)connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        SeriesPartSortKey.Register((SqliteConnection)connection);
        return ValueTask.FromResult(result);
    }
}