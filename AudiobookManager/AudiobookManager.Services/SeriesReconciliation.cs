using System.Collections.Concurrent;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The computed result of reconciling a matched series' roster against its owned books - exactly
/// what the detail page's missing/ignored sections and the overview counts render. Paged requests
/// slice the cached lists instead of re-reading the roster and every owned book per page.
///
/// The three counts are stored alongside the lists (not derived by the service) so the overview
/// badge and the section totals can never disagree with the sections they summarize.
/// </summary>
public sealed record SeriesReconciliation(
    IReadOnlyList<SeriesExpectedBookInfo> Missing,
    IReadOnlyList<SeriesExpectedBookInfo> Ignored,
    int ExpectedBookCount,
    int OwnedCount,
    IReadOnlyList<string> Authors)
{
    public int MissingBookCount => Missing.Count;
    public int IgnoredBookCount => Ignored.Count;
}

/// <summary>
/// Per-series cache of the <see cref="SeriesReconciliation"/> for the series detail. The detail
/// page must not materialize the whole roster and every owned book of a series on every page
/// request; this cache computes the reconciliation once per series and serves the pages from it.
///
/// The cache is <b>explicitly capacity-bounded</b>: entries evict by least-recent access, and the
/// per-series version cells and single-flight gates are pruned when idle, so nothing in this
/// object grows with the number of series ever opened. Eviction is always safe - a reconciliation
/// is pure data and a gate is never <c>Dispose</c>d, only dereferenced once idle - so an evicted
/// series simply recomputes on its next request.
///
/// Invalidation is write-driven and per-series: any mutation of a series' roster (match, refresh,
/// ignore, omnibus toggle) or of the owned books of a series (add, update, delete - which can
/// change Series/SeriesPart/BookName) calls <see cref="Invalidate"/>. Publish is version-gated,
/// and the version check and the entry write/remove happen under the same lock as
/// <see cref="Invalidate"/>, so an invalidation that lands while a reconciliation is being
/// computed can never let the pre-change result be published for the TTL - no stale-data race.
/// </summary>
public interface ISeriesReconciliationCache
{
    /// <summary>The current generation for this series; captures the point-in-time a compute starts.</summary>
    long GetVersion(string seriesName);

    SeriesReconciliation? Get(string seriesName);

    /// <summary>
    /// Publishes a reconciled result, unless <paramref name="versionAtStart"/> is no longer the
    /// current version for this series (an invalidation intervened). Returns whether published.
    /// </summary>
    bool Set(string seriesName, SeriesReconciliation reconciliation, long versionAtStart);

    /// <summary>Bumps this series' generation and drops its entry: its roster or owned books changed.</summary>
    void Invalidate(string seriesName);

    /// <summary>
    /// Single-flight, capacity-bounded read of a reconciliation: at most one
    /// <paramref name="compute"/> per series runs at a time, concurrent callers wait on the
    /// series' gate and then re-check the cache, and the result is version-gated published for
    /// cache and by <paramref name="compute"/>-owned state. Returns the computed (or cached)
    /// reconciliation.
    /// </summary>
    Task<SeriesReconciliation> GetOrComputeAsync(string seriesName, Func<Task<SeriesReconciliation>> compute);
}

public class SeriesReconciliationCache : ISeriesReconciliationCache
{
    /// <summary>
    /// Safety net for an invalidation path the write-driven guarantee does not cover (a future
    /// writer nobody wired up): entries also age out, keeping stale counts bounded on failure and
    /// bounding the cache's memory. Correctness relies on <see cref="Invalidate(string)"/>, not
    /// on this timer.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The maximum number of series worth of reconciliation entries (and, separately, of version
    /// cells and single-flight gates) this cache keeps. A bound large enough that a real library
    /// never evicts a series mid-browsing, but a hard one: once it is exceeded, the
    /// least-recently-accessed entry is evicted and recomputed lazily on its next request.
    /// </summary>
    public const int DefaultCapacity = 1024;

    private sealed class Entry
    {
        public required long Version;
        public required SeriesReconciliation Reconciliation;
        public required DateTimeOffset CreatedAt;
        public long AccessSequence;
    }

    private sealed class VersionCell
    {
        public long Version;
        public long UpdateSequence;
    }

    private sealed class SeriesGate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public bool InUse = true;
        public long LastUseSequence;
    }

    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, VersionCell> _versions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SeriesGate> _gates = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>
    /// Serializes every mutation: Set (version check + write + eviction), Invalidate (version
    /// bump + remove + prune) and the gate acquire/release bookkeeping. Because Set's version
    /// check and Invalidate's version bump are both under this lock, no interleaving can let a
    /// compute that started before an invalidation publish data the invalidation should have
    /// invalidated. Get/GetVersion stay lock-free (ConcurrentDictionary + Volatile reads).
    /// </summary>
    private readonly object _stateLock = new();

    private long _nextVersion;
    private long _accessCounter;

    public SeriesReconciliationCache(TimeProvider? time = null)
        : this(DefaultCapacity, time)
    {
    }

    public SeriesReconciliationCache(int capacity, TimeProvider? time = null)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }

        _capacity = capacity;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Readonly introspection for tests; not part of the cache contract.</summary>
    internal int Capacity => _capacity;
    internal int EntryCount => _entries.Count;
    internal int VersionCellCount => _versions.Count;
    internal int GateCount => _gates.Count;

    public long GetVersion(string seriesName) =>
        _versions.TryGetValue(seriesName, out var cell) ? Volatile.Read(ref cell.Version) : 0;

    public SeriesReconciliation? Get(string seriesName)
    {
        if (_entries.TryGetValue(seriesName, out var entry)
            && entry.Version == GetVersion(seriesName)
            && _time.GetUtcNow() - entry.CreatedAt <= Ttl)
        {
            // Touch for LRU eviction. A race with a concurrent eviction writes to an orphaned
            // object at worst; the access sequence only orders eviction, never correctness.
            Volatile.Write(ref entry.AccessSequence, NextAccessSequence());
            return entry.Reconciliation;
        }

        return null;
    }

    public bool Set(string seriesName, SeriesReconciliation reconciliation, long versionAtStart)
    {
        lock (_stateLock)
        {
            if (versionAtStart != GetVersion(seriesName))
            {
                return false;
            }

            _entries[seriesName] = new Entry
            {
                Version = versionAtStart,
                Reconciliation = reconciliation,
                CreatedAt = _time.GetUtcNow(),
                AccessSequence = NextAccessSequence(),
            };

            EvictEntriesToCapacity();
            PruneIdleVersionCells();
            return true;
        }
    }

    public void Invalidate(string seriesName)
    {
        lock (_stateLock)
        {
            var newVersion = NextVersion();
            var updateSequence = NextAccessSequence();

            // Create or bump the version cell. Version values come from a global strictly
            // monotonic counter, so a recreated cell can never collide with a value an in-flight
            // compute captured before its previous cell was pruned - a pre-prune publish is
            // always refused, never reused as "current".
            if (_versions.TryGetValue(seriesName, out var cell))
            {
                Volatile.Write(ref cell.Version, newVersion);
                cell.UpdateSequence = updateSequence;
            }
            else
            {
                _versions[seriesName] = new VersionCell { Version = newVersion, UpdateSequence = updateSequence };
            }

            _entries.TryRemove(seriesName, out _);
            PruneIdleVersionCells();
        }
    }

    public async Task<SeriesReconciliation> GetOrComputeAsync(
        string seriesName, Func<Task<SeriesReconciliation>> compute)
    {
        if (Get(seriesName) is { } fresh)
        {
            return fresh;
        }

        // Acquire marks the gate in-use under the state lock, so an idle-gate eviction can never
        // remove a gate a waiter is about to (or already does) hold.
        var gate = AcquireGate(seriesName);
        try
        {
            await gate.Semaphore.WaitAsync();
            try
            {
                // Re-check under the gate: the holder may have just published.
                var versionAtStart = GetVersion(seriesName);
                if (Get(seriesName) is { } rechecked)
                {
                    return rechecked;
                }

                var reconciliation = await compute();
                Set(seriesName, reconciliation, versionAtStart);
                return reconciliation;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            ReleaseGate(gate);
        }
    }

    private SeriesGate AcquireGate(string seriesName)
    {
        lock (_stateLock)
        {
            var gate = _gates.GetOrAdd(seriesName, _ => new SeriesGate());
            gate.InUse = true;
            gate.LastUseSequence = NextAccessSequence();
            return gate;
        }
    }

    /// <summary>
    /// Clears the in-use mark and, if the gate map has overflown, evicts idle gates. The
    /// semaphore is deliberately never <c>Dispose</c>d: an evicted gate is simply dropped from
    /// the map and becomes unreachable (reclaimed by GC), so no waiter can ever observe an
    /// <see cref="ObjectDisposedException"/> - removing the disposal race by construction.
    /// </summary>
    private void ReleaseGate(SeriesGate gate)
    {
        lock (_stateLock)
        {
            gate.InUse = false;
            if (_gates.Count > _capacity)
            {
                EvictIdleGates();
            }
        }
    }

    /// <summary>Drops entries until within capacity, least-recently-accessed first.</summary>
    private void EvictEntriesToCapacity()
    {
        // Expired entries are free targets: drop them first, then fall back to LRU.
        if (_entries.Count > _capacity)
        {
            var now = _time.GetUtcNow();
            foreach (var kvp in _entries)
            {
                if (now - kvp.Value.CreatedAt > Ttl)
                {
                    _entries.TryRemove(kvp.Key, out _);
                }
            }
        }

        while (_entries.Count > _capacity)
        {
            string? victimKey = null;
            var oldestSequence = long.MaxValue;
            foreach (var kvp in _entries)
            {
                var sequence = Volatile.Read(ref kvp.Value.AccessSequence);
                if (sequence < oldestSequence)
                {
                    oldestSequence = sequence;
                    victimKey = kvp.Key;
                }
            }

            if (victimKey is null)
            {
                return;
            }

            _entries.TryRemove(victimKey, out _);
        }
    }

    /// <summary>
    /// Drops idle version cells (series with no live reconcile entry) until within capacity,
    /// least-recently-updated first. A live entry's cell is never pruned (its version must stay
    /// comparable), and the global-ticket recreation below keeps pruned keys safe for in-flight
    /// computes - see <see cref="Invalidate"/>.
    /// </summary>
    private void PruneIdleVersionCells()
    {
        while (_versions.Count > _capacity)
        {
            string? victimKey = null;
            var oldestSequence = long.MaxValue;
            foreach (var kvp in _versions)
            {
                if (_entries.ContainsKey(kvp.Key))
                {
                    continue;
                }

                if (kvp.Value.UpdateSequence < oldestSequence)
                {
                    oldestSequence = kvp.Value.UpdateSequence;
                    victimKey = kvp.Key;
                }
            }

            if (victimKey is null)
            {
                return; // every remaining cell belongs to a live entry - keep them all.
            }

            _versions.TryRemove(victimKey, out _);
        }
    }

    /// <summary>
    /// Drops idle gates until within capacity, least-recently-used first. Busy gates (an in-use
    /// mark means a compute or a waiter holds them) are never evicted, so single-flight is
    /// preserved: eviction only forgets a gate nobody is using. See <see cref="ReleaseGate"/> for
    /// why dropping - not disposing - is the safe cleanup.
    /// </summary>
    private void EvictIdleGates()
    {
        while (_gates.Count > _capacity)
        {
            string? victimKey = null;
            var oldestSequence = long.MaxValue;
            foreach (var kvp in _gates)
            {
                if (kvp.Value.InUse)
                {
                    continue;
                }

                if (kvp.Value.LastUseSequence < oldestSequence)
                {
                    oldestSequence = kvp.Value.LastUseSequence;
                    victimKey = kvp.Key;
                }
            }

            if (victimKey is null)
            {
                return; // every surviving gate is busy; do not break single-flight.
            }

            _gates.TryRemove(victimKey, out _);
        }
    }

    private long NextVersion() => Interlocked.Increment(ref _nextVersion);

    private long NextAccessSequence() => Interlocked.Increment(ref _accessCounter);
}