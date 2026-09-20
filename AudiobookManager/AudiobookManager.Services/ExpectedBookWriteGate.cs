namespace AudiobookManager.Services;

/// <summary>
/// Process-wide gate for every ROSTER REWRITE of the unified <c>expected_books</c> roster. The
/// roster rows are written by BOTH the author side (<see cref="IUpcomingReleaseService.RefreshAuthorRosterAsync"/>
/// and its bulk/match-triggered siblings) and the series side (<see cref="ISeriesService"/>'s
/// match/refresh/delete) - and those scopes share the same rows now (one unified
/// <see cref="AudiobookManager.Database.Models.ExpectedBook"/> serves an author refresh and its
/// series refresh for the same book), so the per-controller static semaphores that used to guard
/// each scope separately cannot protect one scope's read-then-upsert-then-prune against the
/// other's. Every roster REWRITE entry point (the match/refresh/delete/apply operations that
/// read and then replace rows or prune links) takes THIS gate - controllers acquire it
/// (<see cref="TryAcquire"/>) and return 409 when it is busy, and fire-and-forget operations pass
/// it to <see cref="AudiobookManager.Api.Async.BackgroundOperationRunner"/> so the gate is held
/// for the whole background run and released in its finally block.
///
/// The single-row ignore-flag mutations (<c>ExpectedBookRepository.SetIgnoredAsync</c>/
/// <c>SetExpectedBookIgnoredByPersonAsync</c>, <c>SeriesRepository.SetExpectedBookIgnoredAsync</c>)
/// deliberately do NOT take the gate: they are set-based <c>ExecuteUpdateAsync</c> statements on
/// exactly the row the caller's natural key or id resolves, so unlike a tracked read-modify-write
/// they cannot throw a <c>DbUpdateConcurrencyException</c> against a concurrent rewrite's
/// prune/orphan-delete, and they write nothing but the flag - there is no read-then-write
/// sequence for a simultaneous rewrite to interleave with.
///
/// Registered as a singleton (see <c>AudiobookManager.Services.DependencyInjection</c>), so
/// every controller and background task shares one instance process-wide. The gate is
/// deliberately not reentrant and never awaited: a busy gate means the mutation should be
/// refused (409), not deferred.
/// </summary>
public interface IExpectedBookWriteGate
{
    /// <summary>Acquires the gate without blocking; false when another roster mutation holds it.</summary>
    bool TryAcquire();

    /// <summary>Releases the gate. Only the holder may release it, and exactly once.</summary>
    void Release();
}

/// <summary>
/// See <see cref="IExpectedBookWriteGate"/>. A single <see cref="SemaphoreSlim"/> started held by
/// no one; <see cref="TryAcquire"/> never blocks, so a busy mutation is refused with 409 rather
/// than consumed by a queue.
/// </summary>
public sealed class ExpectedBookWriteGate : IExpectedBookWriteGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool TryAcquire() => _semaphore.Wait(0);

    public void Release() => _semaphore.Release();
}