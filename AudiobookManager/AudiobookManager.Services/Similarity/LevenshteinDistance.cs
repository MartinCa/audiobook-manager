namespace AudiobookManager.Services.Similarity;

/// <summary>
/// Standalone Levenshtein (edit distance) implementation - no external dependency needed
/// for the small strings (author names, series names) this feature compares.
/// </summary>
public static class LevenshteinDistance
{
    public static int Compute(string a, string b)
    {
        if (string.IsNullOrEmpty(a))
            return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b))
            return a.Length;

        var lengthA = a.Length;
        var lengthB = b.Length;
        var distances = new int[lengthA + 1, lengthB + 1];

        for (var i = 0; i <= lengthA; i++)
            distances[i, 0] = i;
        for (var j = 0; j <= lengthB; j++)
            distances[0, j] = j;

        for (var i = 1; i <= lengthA; i++)
        {
            for (var j = 1; j <= lengthB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[lengthA, lengthB];
    }
}
