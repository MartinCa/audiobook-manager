using System.Collections.Concurrent;

namespace AudiobookManager.Api.Async;

/// <summary>
/// Current state of a long-running, lock-guarded background operation (library scan,
/// bulk import, consistency check, similar-value alignment, series match/refresh).
/// </summary>
public record OperationStatus(bool IsRunning, int Processed, int Total);

/// <summary>
/// In-memory, per-instance record of the current status of each background operation, keyed
/// by a stable operation name. SignalR progress/complete events are broadcast-only and can be
/// missed by a client that is disconnected or not yet mounted, so this registry lets a client
/// fetch the authoritative current state on mount or after reconnecting instead of trusting
/// only events that may never arrive.
/// </summary>
public interface IOperationStatusRegistry
{
    OperationStatus GetStatus(string key);
    void SetRunning(string key);
    void SetProgress(string key, int processed, int total);
    void SetFinished(string key);
}

public class OperationStatusRegistry : IOperationStatusRegistry
{
    private static readonly OperationStatus NotRunning = new(false, 0, 0);

    private readonly ConcurrentDictionary<string, OperationStatus> _statuses = new();

    public OperationStatus GetStatus(string key) =>
        _statuses.TryGetValue(key, out var status) ? status : NotRunning;

    public void SetRunning(string key) => _statuses[key] = new OperationStatus(true, 0, 0);

    public void SetProgress(string key, int processed, int total) => _statuses[key] = new OperationStatus(true, processed, total);

    public void SetFinished(string key) => _statuses[key] = NotRunning;
}
