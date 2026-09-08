using System.Collections.Concurrent;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// In-memory cache of the detected near-duplicate groups (see <see cref="ISimilarValueService"/>).
/// Detection was stateless - every page request re-read every distinct value and re-clustered the
/// whole library - so paging through a few hundred groups re-ran the same clustering once per
/// request. Caching the computed name clusters makes the detection itself bounded: requests
/// within the TTL are served from the cache, and the cache is invalidated the moment an alignment
/// folds two values into one. Book counts are never cached - they are re-read per page - so the
/// only staleness window is the group *structure* (up to the TTL after a library change that
/// added/renamed a value), never a displayed number.
///
/// Publish is version-gated: <see cref="Set"/> takes the version that existed when the caller
/// began computing, and drops the result if an <see cref="Invalidate"/> happened in the
/// meantime. Without that, a compute that read the library before an alignment committed would
/// publish its pre-alignment groups into the cache after <see cref="Invalidate"/> had cleared
/// it, resurrecting stale groups for the whole TTL. A dropped publish costs one recompute on the
/// next request; no pre-invalidation data is ever served from the cache again.
/// </summary>
public interface ISimilarValueDetectionCache
{
    /// <summary>The current generation; captures the point-in-time a compute starts.</summary>
    long GetVersion();

    List<SimilarValueGroup>? Get(string kind);

    /// <summary>
    /// Publishes a computed result, unless <paramref name="versionAtStart"/> is no longer the
    /// current version (an invalidation intervened). Returns whether the value was published.
    /// </summary>
    bool Set(string kind, List<SimilarValueGroup> groups, long versionAtStart);

    /// <summary>Bumps the generation and drops every stored group: alignment merges values, so each stored group may be stale.</summary>
    void Invalidate();
}

public class SimilarValueDetectionCache : ISimilarValueDetectionCache
{
    /// <summary>Bounded staleness for library changes detection is not notified about (a scan, a save).</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed record Entry(long Version, List<SimilarValueGroup> Groups, DateTimeOffset CreatedAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _time;
    private long _version;

    public SimilarValueDetectionCache(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public long GetVersion() => Interlocked.Read(ref _version);

    public List<SimilarValueGroup>? Get(string kind)
    {
        var currentVersion = GetVersion();
        if (_entries.TryGetValue(kind, out var entry)
            && entry.Version == currentVersion
            && _time.GetUtcNow() - entry.CreatedAt <= Ttl)
        {
            return entry.Groups;
        }

        return null;
    }

    public bool Set(string kind, List<SimilarValueGroup> groups, long versionAtStart)
    {
        // The caller captured versionAtStart before it began computing. If an invalidation ran
        // since (including one concurrent with this publish), the computed groups may predate
        // it - drop them rather than republishing stale groups for the TTL.
        if (versionAtStart != GetVersion())
        {
            return false;
        }

        _entries[kind] = new Entry(versionAtStart, groups, _time.GetUtcNow());
        return true;
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _entries.Clear();
    }
}