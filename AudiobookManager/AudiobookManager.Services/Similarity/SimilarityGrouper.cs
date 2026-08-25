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
    /// </summary>
    public static List<List<string>> GroupSimilarValues(IReadOnlyList<string> values, AudiobookManagerSettings settings)
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

    private static int GetMaxDistance(int length, AudiobookManagerSettings settings)
    {
        if (length <= settings.SimilarityShortLength)
            return 0;
        if (length <= settings.SimilarityMediumLength)
            return settings.SimilarityMaxDistanceMedium;
        return settings.SimilarityMaxDistanceLong;
    }
}
