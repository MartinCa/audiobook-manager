namespace AudiobookManager.Api.Async;

/// <summary>
/// The process-wide gates shared by more than one controller endpoint.
///
/// A combined library scan and a consistency check are the same operation now - one background
/// run that both walks the library and rewrites the issue tables - so the controller endpoints
/// that start them (<c>LibraryController._scanLock</c>,
/// <c>ConsistencyController._checkLock</c>) must be guarded by the same semaphore, or a
/// scan and a consistency run could both be in flight against the same files and tables.
/// </summary>
public static class BackgroundOperationGates
{
    public static readonly SemaphoreSlim LibraryScanAndConsistency = new(1, 1);
}
