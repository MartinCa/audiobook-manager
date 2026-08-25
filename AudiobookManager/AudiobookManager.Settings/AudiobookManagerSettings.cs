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
}
