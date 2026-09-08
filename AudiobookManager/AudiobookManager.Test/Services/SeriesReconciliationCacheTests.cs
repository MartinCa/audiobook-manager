using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The per-series reconciliation cache: version-gated publish (an invalidation concurrent with
/// an in-flight compute must not let pre-change data be cached), per-series isolation, and TTL
/// expiry. Uses a fake <see cref="TimeProvider"/> so expiry is deterministic.
/// </summary>
[TestClass]
public class SeriesReconciliationCacheTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record R(int Value);

    private FakeTimeProvider _time = null!;
    private SeriesReconciliationCache _cache = null!;

    /// <summary>A generic reconciliation-shaped value; the specific field values are irrelevant to the cache contract.</summary>
    private static SeriesReconciliation MakeReconciliation(int tag = 0) =>
        new(new List<SeriesExpectedBookInfo>(), new List<SeriesExpectedBookInfo>(), 0, 0, new[] { $"Author {tag}" });

    [TestInitialize]
    public void Setup()
    {
        _time = new FakeTimeProvider();
        _cache = new SeriesReconciliationCache(_time);
    }

    [TestMethod]
    public void Set_AfterAnInvalidationConcurrentWithTheCompute_IsDropped()
    {
        // A compute starts (capturing the version), and while it is in flight the series' roster
        // or owned books change, invalidating the cache. The late publish must be refused, or
        // pre-change data would be served for the TTL.
        var versionAtStart = _cache.GetVersion("Mistborn");
        _cache.Invalidate("Mistborn");

        var published = _cache.Set("Mistborn", MakeReconciliation(), versionAtStart);

        Assert.IsFalse(published, "the pre-invalidation compute must not be published");
        Assert.IsNull(_cache.Get("Mistborn"), "nothing may be served from the refused publish");
    }

    [TestMethod]
    public void Get_StaysEmptyUntilAComputeStartedAfterTheInvalidationPublishes()
    {
        _cache.Invalidate("Mistborn");

        // A compute that began *after* the invalidation sees the new generation and publishes.
        var versionAfterInvalidation = _cache.GetVersion("Mistborn");
        var published = _cache.Set("Mistborn", MakeReconciliation(), versionAfterInvalidation);

        Assert.IsTrue(published, "a compute that saw the invalidated generation may publish");
        Assert.IsNotNull(_cache.Get("Mistborn"));
    }

    [TestMethod]
    public void Invalidate_IsPerSeries_OtherSeriesStayServed()
    {
        Assert.IsTrue(_cache.Set("Mistborn", MakeReconciliation(), 0));
        Assert.IsTrue(_cache.Set("Stormlight", MakeReconciliation(1), 0));

        _cache.Invalidate("Mistborn");

        Assert.IsNull(_cache.Get("Mistborn"));
        Assert.IsNotNull(_cache.Get("Stormlight"), "an unrelated series' entry must survive");
    }

    [TestMethod]
    public void Get_IsAlsoVersionChecked_AfterAnInvalidateWithoutARemove()
    {
        // Even if an entry survived an invalidation (it never does in this implementation, but
        // the version check is what guarantees it), it must not be served.
        Assert.IsTrue(_cache.Set("Mistborn", MakeReconciliation(), 0));
        _cache.Invalidate("Mistborn");

        Assert.IsNull(_cache.Get("Mistborn"),
            "the entry must not be served once its generation is stale");
    }

    [TestMethod]
    public void Get_ExpiredEntry_IsTreatedAsAMiss()
    {
        Assert.IsTrue(_cache.Set("Mistborn", MakeReconciliation(), 0));

        _time.Now = _time.Now.AddMinutes(11);

        Assert.IsNull(_cache.Get("Mistborn"), "the TTL is the safety net for an unwired invalidation");
    }

    [TestMethod]
    public void Set_WithTheCurrentVersion_PublishesAndIsServed()
    {
        var version = _cache.GetVersion("Mistborn");

        Assert.IsTrue(_cache.Set("Mistborn", MakeReconciliation(7), version));
        Assert.AreEqual("Author 7", _cache.Get("Mistborn")!.Authors.Single());
    }

    // Capacity bound, LRU eviction: the least-recently-accessed entry is dropped when a Set
    // overflows. The access clock is bumped by Get, so the order is deterministic.
    [TestMethod]
    public void Set_OverCapacity_EvictsTheLeastRecentlyAccessedEntry()
    {
        var cache = new SeriesReconciliationCache(capacity: 2, _time);

        Assert.IsTrue(cache.Set("A", MakeReconciliation(1), 0));
        Assert.IsTrue(cache.Set("B", MakeReconciliation(2), 0));
        Assert.IsNotNull(cache.Get("A"), "touch A so it is the most recent");
        Assert.IsTrue(cache.Set("C", MakeReconciliation(3), 0));

        Assert.AreEqual(2, cache.EntryCount, "the cache must never exceed its capacity");
        Assert.IsNotNull(cache.Get("A"), "most recently touched entries survive");
        Assert.IsNotNull(cache.Get("C"));
        Assert.IsNull(cache.Get("B"), "the least-recently-accessed entry is evicted");
    }

    // Capacity bound is exact, not "about": inserting past it evicts down to capacity.
    [TestMethod]
    public void Set_OverCapacity_KeepsTheCountExactlyAtCapacity()
    {
        var cache = new SeriesReconciliationCache(capacity: 3, _time);
        for (var i = 0; i < 25; i++)
        {
            Assert.IsTrue(cache.Set($"S{i:00}", MakeReconciliation(i), 0));
            Assert.IsTrue(cache.EntryCount <= 3);
        }

        Assert.AreEqual(3, cache.EntryCount);
    }

    // Eviction is safe: an evicted entry is simply gone (a miss recomputes); nothing fatal, and
    // the survivors keep serving.
    [TestMethod]
    public void Set_Eviction_DoesNotCorruptTheSurvivingEntries()
    {
        var cache = new SeriesReconciliationCache(capacity: 1, _time);

        Assert.IsTrue(cache.Set("A", MakeReconciliation(1), 0));
        Assert.IsTrue(cache.Set("B", MakeReconciliation(2), 0));

        Assert.IsNull(cache.Get("A"), "A was evicted - a miss, not an error");
        Assert.AreEqual("Author 2", cache.Get("B")!.Authors.Single());
    }

    // Version cells are pruned when idle (no live entry) and the map overflows. Pruning must not
    // open a stale-publish window: version values come from a global monotonic counter, so a
    // compute that captured a *pruned* version can never smuggle its publish past a later
    // recreation - the recreated cell holds a strictly larger value.
    [TestMethod]
    public void PruneIdleVersionCells_RecreatedCell_RefusesPrePruneCaptures()
    {
        var cache = new SeriesReconciliationCache(capacity: 2, _time);

        // Invalidate three series. With capacity 2, the oldest idle cells (A, then B) are pruned
        // as each overflow lands; whoever survives must still hold its current value.
        cache.Invalidate("A");
        cache.Invalidate("B");

        var captureBeforePrune = cache.GetVersion("A");
        cache.Invalidate("C"); // overflows the cell map - A or B (idle, oldest) gets pruned.

        Assert.AreEqual(2, cache.VersionCellCount, "idle cells are pruned down to capacity");
        Assert.IsFalse(cache.Set("A", MakeReconciliation(), captureBeforePrune),
            "a pre-prune captured version must not equal a recreated cell");

        // A fresh capture after recreation publishes normally.
        cache.Invalidate("A");
        var fresh = cache.GetVersion("A");
        Assert.IsTrue(cache.Set("A", MakeReconciliation(), fresh));
    }

    [TestMethod]
    public async Task GateEviction_SkipsInFlightSeries_AndSecondCallerSharesTheComputation()
    {
        var cache = new SeriesReconciliationCache(capacity: 2, _time);
        var computeStarted = new TaskCompletionSource();
        var releaseCompute = new TaskCompletionSource();
        var aComputes = 0;

        async Task<SeriesReconciliation> ComputeA()
        {
            Interlocked.Increment(ref aComputes);
            computeStarted.SetResult();
            await releaseCompute.Task;
            return MakeReconciliation(1);
        }

        // Caller 1 starts reconciling A and blocks mid-compute, holding A's gate.
        var caller1 = cache.GetOrComputeAsync("A", ComputeA);
        await computeStarted.Task;

        // Two other series fill the gate map to its capacity of 2 and then overflow it. Their
        // computes finish quickly, so their gates are idle - the overflow eviction drops the
        // oldest *idle* gate, NOT A's in-flight one.
        await cache.GetOrComputeAsync("B", () => Task.FromResult(MakeReconciliation(2)));
        await cache.GetOrComputeAsync("C", () => Task.FromResult(MakeReconciliation(3)));

        Assert.AreEqual(2, cache.GateCount, "idle gates were evicted down to capacity");
        Assert.AreEqual(1, aComputes, "A's compute is still in flight; its gate must survive");

        // Caller 2 for A arrives while caller 1 is still computing: it must wait on A's (now
        // in-use) gate and, once caller 1 publishes, return the cached result WITHOUT recomputing.
        var caller2 = cache.GetOrComputeAsync("A", ComputeA);

        releaseCompute.SetResult();
        await Task.WhenAll(caller1, caller2).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.AreEqual(1, aComputes,
            "single-flight held across gate eviction: caller 2 must not recompute");
        Assert.AreEqual("Author 1", cache.Get("A")!.Authors.Single(),
            "the in-flight computation's result is published and served intact");
        Assert.AreEqual(2, cache.GateCount, "the gate map stays bounded after everyone finishes");
    }

    // The gates hold only SemaphoreSlim objects that are never disposed - eviction just drops
    // the map entry. A series that was evicted (idle) then becomes hot again must simply start a
    // fresh gate and work normally: no disposal race, no corruption.
    [TestMethod]
    public async Task GateEviction_EvictedSeries_RestartsCleanlyOnItsNextRequest()
    {
        var cache = new SeriesReconciliationCache(capacity: 1, _time);

        await cache.GetOrComputeAsync("A", () => Task.FromResult(MakeReconciliation(1)));

        // Overflow evicts A's idle gate.
        await cache.GetOrComputeAsync("B", () => Task.FromResult(MakeReconciliation(2)));
        Assert.AreEqual(1, cache.GateCount);

        var again = await cache.GetOrComputeAsync("A", () => Task.FromResult(MakeReconciliation(3)));
        Assert.AreEqual("Author 3", again.Authors.Single(), "a fresh gate works without disposal errors");
        Assert.AreEqual(1, cache.GateCount);
        Assert.AreEqual(1, cache.EntryCount);
    }
}