namespace AudiobookManager.Api.Dtos;

public record SeriesOverviewDto(
    long? Id,
    string Name,
    List<string> Authors,
    int OwnedBookCount,
    bool IsMatched,
    string? MatchedSourceName,
    string? MatchedSourceId,
    string? MatchedSourceUrl,
    double? MatchConfidence,
    DateTime? LastRefreshedAt,
    int ExpectedBookCount,
    int MissingBookCount,
    int IgnoredBookCount
);

public record SeriesExpectedBookDto(
    long Id,
    string Title,
    string? Position,
    int? Year,
    string? SourceUrl,
    bool IsIgnored
);

public record SeriesOwnedBookDto(
    long Id,
    string BookName,
    string? SeriesPart,
    int Year,
    List<string> Authors,
    List<string> Narrators,
    int? DurationInSeconds
);

public record SeriesDetailDto(
    SeriesOverviewDto Overview,
    List<SeriesOwnedBookDto> OwnedBooks,
    List<SeriesExpectedBookDto> MissingBooks,
    List<SeriesExpectedBookDto> IgnoredBooks
);

public record SeriesMatchCandidateDto(
    string SourceName,
    string SourceId,
    string SeriesName,
    string? SourceUrl,
    List<string> Authors,
    int? BookCount,
    double Confidence
);

public class MatchSeriesDto
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public double? Confidence { get; set; }
}

public class BulkMatchSeriesDto
{
    public double ConfidenceThreshold { get; set; } = 0.85;

    /// <summary>
    /// Optional subset of series names to auto-match. When null or empty, every unmatched
    /// series is considered.
    /// </summary>
    public List<string>? SeriesNames { get; set; }
}
