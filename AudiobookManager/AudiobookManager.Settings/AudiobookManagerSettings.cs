namespace AudiobookManager.Settings;
public class AudiobookManagerSettings
{
    public string AudiobookImportPath { get; set; }
    public string AudiobookLibraryPath { get; set; }
    public string DbLocation { get; set; } = "/config/audiobookmanager.db";
    public string? HardcoverApiKey { get; set; }

    // Similar-value (author/series) fuzzy matching thresholds. Conservative defaults:
    // strings this short or shorter require an exact normalized match (no edit-distance
    // slack, to avoid false positives between short distinct names); strings up to
    // SimilarityMediumLength allow a single-character edit distance; anything longer
    // allows up to SimilarityMaxDistanceLong.
    public int SimilarityShortLength { get; set; } = 4;
    public int SimilarityMediumLength { get; set; } = 8;
    public int SimilarityMaxDistanceMedium { get; set; } = 1;
    public int SimilarityMaxDistanceLong { get; set; } = 2;

    // Hardcover API client-side rate limiting. Hardcover's Free plan (the only tier this app
    // models) documents 5,000 requests/day, a burst of 10 and 60 requests/minute. The
    // defaults below stay strictly under those: the token bucket holds at most
    // HardcoverBurstLimit tokens and refills HardcoverPerMinuteLimit tokens per minute, so
    // the worst case in any rolling minute is burst + per-minute, which must not exceed the
    // hard API ceiling of 60 (validated at startup - see HardcoverRateLimiter).
    public int HardcoverDailyRequestLimit { get; set; } = 5000;
    public int HardcoverBurstLimit { get; set; } = 5;
    public int HardcoverPerMinuteLimit { get; set; } = 55;
}
