using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// Version-gated publish of the similar-values detection cache: an <see cref="Invalidate"/>
/// landing while a compute is in flight must refuse the late publish, so pre-alignment groups
/// are never written back into the cache for the TTL. Direct, deterministic tests of the gate
/// itself; <c>SimilarValueServiceTests</c> exercises the same race end-to-end via task
/// coordination.
/// </summary>
[TestClass]
public class SimilarValueDetectionCacheTests
{
    private SimilarValueDetectionCache _cache = null!;

    [TestInitialize]
    public void Setup()
    {
        _cache = new SimilarValueDetectionCache();
    }

    private static List<SimilarValueGroup> MakeGroups() => new()
    {
        new SimilarValueGroup
        {
            Candidates = new List<SimilarValueCandidate> { new() { Value = "J.K. Rowling" } },
        },
    };

    // The regression guard for the stale-publication race: a compute that captured the version
    // before an invalidation must not be able to publish its (pre-invalidation) result.
    [TestMethod]
    public void Set_CapturedBeforeAnInvalidation_IsDropped()
    {
        var versionAtComputeStart = _cache.GetVersion();
        _cache.Invalidate();

        var published = _cache.Set("authors", MakeGroups(), versionAtComputeStart);

        Assert.IsFalse(published, "the pre-invalidation compute's publish must be refused");
        Assert.IsNull(_cache.Get("authors"), "the refused publish must not be served");
    }

    [TestMethod]
    public void Set_CapturedAfterTheInvalidation_IsPublished()
    {
        _cache.Invalidate();

        var versionAfterInvalidation = _cache.GetVersion();
        var published = _cache.Set("authors", MakeGroups(), versionAfterInvalidation);

        Assert.IsTrue(published, "a compute that began after the invalidation publishes normally");
        Assert.IsNotNull(_cache.Get("authors"));
    }

    [TestMethod]
    public void Get_DoesNotServeAnEntryWhoseGenerationIsStale()
    {
        Assert.IsTrue(_cache.Set("authors", MakeGroups(), _cache.GetVersion()));

        _cache.Invalidate();

        Assert.IsNull(_cache.Get("authors"),
            "even an entry that survived an invalidation would be refused by the version check");
    }
}