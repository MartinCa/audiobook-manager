using AudiobookManager.Settings;

namespace AudiobookManager.Services.Similarity;

/// <summary>
/// Clusters a list of distinct raw strings (author names, series names, etc.) into
/// groups of near-duplicates, using normalized-equality or a length-scaled edit-distance
/// threshold. Union-find with length-bucketed blocking keeps this well under O(n^2) for
/// realistic library sizes.
/// </summary>
public static class SimilarityGrouper
{
    /// <summary>
    /// Groups near-duplicate values. Only clusters with more than one member are returned.
    /// Input values are expected to already be distinct (case-sensitive); order of values
    /// within a cluster is preserved from the input.
    ///
    /// <paramref name="isSeries"/> turns on a series-only rule: two values whose normalized form
    /// differs only by a leading "the " (<see cref="NameNormalizer.StripLeadingArticle"/>) are
    /// unioned directly, in a separate O(n) bucketing pass independent of the length-blocking
    /// loop below - "The Mistborn Saga" vs "Mistborn Saga" differs by 4 characters, which can
    /// fall outside the edit-distance threshold and the length-window blocking cutoff both.
    /// Never applied to authors, so an author literally named "The Rock" does not fold onto
    /// "Rock".
    ///
    /// <paramref name="ignoredPairs"/> removes specific edges the union-find step would
    /// otherwise draw: a pair explicitly marked "not similar" is skipped, but the two values can
    /// still end up in the same cluster transitively through a third value neither is ignored
    /// against - this only removes that one edge, not the values from consideration entirely.
    /// Pairs are looked up with <see cref="IgnoredPairKey"/> (ValueA/ValueB ordered the same way
    /// the caller normalizes them before building the set).
    /// </summary>
    public static List<List<string>> GroupSimilarValues(
        IReadOnlyList<string> values,
        AudiobookManagerSettings settings,
        bool isSeries = false,
        HashSet<(string A, string B)>? ignoredPairs = null)
    {
        var n = values.Count;
        if (n < 2)
            return new List<List<string>>();

        var normalized = new string[n];
        for (var i = 0; i < n; i++)
            normalized[i] = NameNormalizer.Normalize(values[i]);

        // Sort indices by normalized length to bound the comparison window (blocking).
        var order = Enumerable.Range(0, n)
            .OrderBy(i => normalized[i].Length)
            .ToArray();

        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        var maxWindow = Math.Max(settings.SimilarityMaxDistanceMedium, settings.SimilarityMaxDistanceLong);

        for (var oi = 0; oi < order.Length; oi++)
        {
            var i = order[oi];
            var normI = normalized[i];
            if (normI.Length == 0)
                continue;

            for (var oj = oi + 1; oj < order.Length; oj++)
            {
                var j = order[oj];
                var normJ = normalized[j];

                // Bounded by length difference - normalized strings are sorted by length,
                // so once the gap exceeds the widest possible threshold, no later entry
                // can match either (blocking to avoid O(n^2)).
                if (normJ.Length - normI.Length > maxWindow)
                    break;

                if (Find(i) == Find(j))
                    continue;

                if (ignoredPairs is { Count: > 0 } && ignoredPairs.Contains(IgnoredPairKey(values[i], values[j])))
                    continue;

                if (normI == normJ)
                {
                    Union(i, j);
                    continue;
                }

                var threshold = GetMaxDistance(Math.Min(normI.Length, normJ.Length), settings);
                if (threshold <= 0)
                    continue;

                var distance = LevenshteinDistance.Compute(normI, normJ);
                if (distance <= threshold)
                    Union(i, j);
            }
        }

        if (isSeries)
        {
            // Independent of the length-blocking loop above (and its window-break cutoff), so a
            // leading-article difference is caught even when it puts the pair outside that
            // window. A bucket of size 1 unions nothing, so running this unconditionally for
            // every series value is simpler than special-casing which ones to check.
            var strippedBuckets = new Dictionary<string, List<int>>();
            for (var i = 0; i < n; i++)
            {
                var stripped = NameNormalizer.StripLeadingArticle(normalized[i]);
                if (stripped.Length == 0)
                    continue;

                if (!strippedBuckets.TryGetValue(stripped, out var bucket))
                {
                    bucket = new List<int>();
                    strippedBuckets[stripped] = bucket;
                }
                bucket.Add(i);
            }

            foreach (var bucket in strippedBuckets.Values)
            {
                for (var k = 1; k < bucket.Count; k++)
                {
                    var i = bucket[0];
                    var j = bucket[k];
                    if (Find(i) == Find(j))
                        continue;
                    if (ignoredPairs is { Count: > 0 } && ignoredPairs.Contains(IgnoredPairKey(values[i], values[j])))
                        continue;
                    Union(i, j);
                }
            }
        }

        var groups = new Dictionary<int, List<string>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = new List<string>();
                groups[root] = list;
            }
            list.Add(values[i]);
        }

        return groups.Values.Where(g => g.Count > 1).ToList();
    }

    /// <summary>
    /// Orders a pair of raw values the same way an ignored-pair row is stored (ValueA &lt;
    /// ValueB by <see cref="StringComparer.Ordinal"/>), so a pair is unordered for lookup
    /// purposes regardless of which order it was detected in.
    /// </summary>
    public static (string A, string B) IgnoredPairKey(string value1, string value2) =>
        StringComparer.Ordinal.Compare(value1, value2) <= 0 ? (value1, value2) : (value2, value1);

    private static int GetMaxDistance(int length, AudiobookManagerSettings settings)
    {
        if (length <= settings.SimilarityShortLength)
            return 0;
        if (length <= settings.SimilarityMediumLength)
            return settings.SimilarityMaxDistanceMedium;
        return settings.SimilarityMaxDistanceLong;
    }
}
