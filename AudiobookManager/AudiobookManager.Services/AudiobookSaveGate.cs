using System.Collections.Concurrent;

namespace AudiobookManager.Services;

/// <summary>
/// Thrown when an operation that rewrites an audiobook's file is refused because another one is
/// already in flight for that book. Bulk callers let it surface as a single failed item; the
/// save endpoint turns it into a 409.
/// </summary>
public class AudiobookBusyException : Exception
{
    public long AudiobookId { get; }

    public AudiobookBusyException(long audiobookId)
        : base($"Another operation is already modifying audiobook {audiobookId}")
    {
        AudiobookId = audiobookId;
    }
}

/// <summary>
/// Process-wide mutual exclusion, per audiobook, for the operations that rewrite an m4b's tags
/// or move its file.
///
/// Two of those running at once for the same book corrupts it: both read the same pre-move path,
/// the first relocates the file, and the second then writes tags to a path that no longer exists
/// (or fails with a spurious "already exists"). The interactive save endpoint has been gated
/// since it was written, but it held its own private set - so it excluded only *other saves*.
/// A consistency bulk resolve and a similar-value alignment both rewrite the same files through
/// the same service and were not gated against it, or against each other: `resolve-by-type` for
/// TagMismatch has no lock of its own at all, so two of those could run concurrently over the
/// same books.
///
/// Acquisition is deliberately non-blocking - a second operation is refused, never queued - so
/// this is a set of busy ids rather than a dictionary of semaphores, which would otherwise
/// accumulate one entry per book ever touched.
/// </summary>
public interface IAudiobookSaveGate
{
    /// <summary>Whether an operation currently holds this book.</summary>
    bool IsBusy(long audiobookId);

    /// <summary>
    /// Takes the gate, or returns false without waiting. Dispose the lease to release it.
    /// </summary>
    bool TryAcquire(long audiobookId, out IDisposable lease);

    /// <summary>
    /// Takes the gate, throwing <see cref="AudiobookBusyException"/> if another operation holds
    /// it. For the bulk paths, whose per-item try/catch turns that into one counted failure
    /// rather than an aborted batch.
    /// </summary>
    IDisposable Acquire(long audiobookId);
}

public class AudiobookSaveGate : IAudiobookSaveGate
{
    // Instance state behind a singleton registration, the same shape as IOperationStatusRegistry:
    // the gate is only an exclusion if every caller in the process shares one instance, which is
    // what SetupServiceLayer's AddSingleton guarantees (and DependencyInjectionTests asserts).
    // Deliberately not static - that would make it shared between unit tests too, so one test's
    // audiobook id could refuse another's.
    private readonly ConcurrentDictionary<long, byte> _busy = new();

    public bool IsBusy(long audiobookId) => _busy.ContainsKey(audiobookId);

    public bool TryAcquire(long audiobookId, out IDisposable lease)
    {
        if (!_busy.TryAdd(audiobookId, 0))
        {
            lease = NullLease.Instance;
            return false;
        }

        lease = new Lease(_busy, audiobookId);
        return true;
    }

    public IDisposable Acquire(long audiobookId) =>
        TryAcquire(audiobookId, out var lease) ? lease : throw new AudiobookBusyException(audiobookId);

    private sealed class Lease : IDisposable
    {
        private readonly ConcurrentDictionary<long, byte> _busy;
        private readonly long _audiobookId;
        private bool _released;

        public Lease(ConcurrentDictionary<long, byte> busy, long audiobookId)
        {
            _busy = busy;
            _audiobookId = audiobookId;
        }

        public void Dispose()
        {
            // Idempotent: a lease disposed twice must not release a gate a *later* operation for
            // the same book has since taken.
            if (_released)
            {
                return;
            }

            _released = true;
            _busy.TryRemove(_audiobookId, out _);
        }
    }

    private sealed class NullLease : IDisposable
    {
        public static readonly NullLease Instance = new();

        public void Dispose()
        {
        }
    }
}
