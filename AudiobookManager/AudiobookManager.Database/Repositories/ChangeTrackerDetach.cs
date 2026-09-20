using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The shared "detach tracked entries after a set-based write" helper. <c>ExecuteDeleteAsync</c>/
/// <c>ExecuteUpdateAsync</c> bypass the change tracker, which has two consequences (see the
/// "Bulk EF operations and the change tracker" note in AGENTS.md): rows this context already
/// loaded stay in the identity map, so SQLite-reused rowids can resolve back to stale entities,
/// and a tracked copy would overwrite the set-based change on the next <c>SaveChanges</c>. Every
/// caller that performs a set-based write over a table this context may have loaded must run its
/// affected tracked entries through <see cref="DetachTracked"/> - this is that pattern in one
/// place, instead of an inlined <c>foreach</c> per call site.
/// </summary>
internal static class ChangeTrackerDetach
{
    /// <summary>
    /// Detaches every tracked <typeparamref name="TEntity"/> entry whose entity satisfies
    /// <paramref name="predicate"/>. The entries are snapshotted before detaching - iteration
    /// over the live change-tracker collection while mutating entry state is not safe.
    /// </summary>
    public static void DetachTracked<TEntity>(IEnumerable<EntityEntry<TEntity>> entries, Func<TEntity, bool> predicate)
        where TEntity : class
    {
        foreach (var entry in entries.Where(e => predicate(e.Entity)).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}