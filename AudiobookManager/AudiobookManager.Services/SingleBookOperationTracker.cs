using System.Collections.Concurrent;

namespace AudiobookManager.Services;

/// <summary>
/// Which single-book operations are in flight right now, keyed by operation name and audiobook id.
///
/// This is separate from <see cref="IAudiobookSaveGate"/> on purpose. The save gate only covers the
/// part that rewrites the file, and it is taken *inside* the service (non-reentrant), so a controller
/// cannot hold it across the whole request. The operations this tracks have work before that point
/// (fetching a candidate from a source) or are started twice by a double click, and a second request
/// for the same book and operation must be refused up front rather than run alongside the first.
/// Acquisition never blocks, mirroring the save gate, and the tracker is a singleton so every request
/// shares one set.
/// </summary>
public interface ISingleBookOperationTracker
{
    /// <summary>Takes the slot, or returns false without waiting. Dispose the lease to release it.</summary>
    bool TryBegin(string operation, long audiobookId, out IDisposable lease);

    /// <summary>Whether this operation is running for the book.</summary>
    bool IsRunning(string operation, long audiobookId);
}

public class SingleBookOperationTracker : ISingleBookOperationTracker
{
    private readonly ConcurrentDictionary<(string Operation, long AudiobookId), byte> _running = new();

    public bool IsRunning(string operation, long audiobookId) => _running.ContainsKey((operation, audiobookId));

    public bool TryBegin(string operation, long audiobookId, out IDisposable lease)
    {
        var key = (operation, audiobookId);
        if (!_running.TryAdd(key, 0))
        {
            lease = NullLease.Instance;
            return false;
        }

        lease = new Lease(_running, key);
        return true;
    }

    private sealed class Lease : IDisposable
    {
        private readonly ConcurrentDictionary<(string, long), byte> _running;
        private readonly (string, long) _key;
        private bool _released;

        public Lease(ConcurrentDictionary<(string, long), byte> running, (string, long) key)
        {
            _running = running;
            _key = key;
        }

        public void Dispose()
        {
            // Idempotent, so a double dispose cannot release a slot a later operation has taken.
            if (_released)
            {
                return;
            }

            _released = true;
            _running.TryRemove(_key, out _);
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
