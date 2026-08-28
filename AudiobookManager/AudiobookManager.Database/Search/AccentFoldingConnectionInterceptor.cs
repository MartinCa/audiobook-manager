using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AudiobookManager.Database.Search;

/// <summary>
/// Registering a SQLite custom function on a connection can only happen once EF Core has
/// actually created the ADO.NET connection object, which is not yet true inside
/// <c>DbContext.OnConfiguring</c> - the context is still being configured at that point, and
/// touching <c>Database.GetDbConnection()</c> there throws. A connection interceptor is the
/// supported hook: it fires right as a (possibly pooled/reused) connection is about to open, so
/// the function is always registered before any query can run against it.
/// </summary>
public sealed class AccentFoldingConnectionInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        AccentFolding.Register((SqliteConnection)connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        AccentFolding.Register((SqliteConnection)connection);
        return ValueTask.FromResult(result);
    }
}
