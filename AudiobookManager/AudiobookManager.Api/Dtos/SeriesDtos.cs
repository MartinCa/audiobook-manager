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
    int IgnoredBookCount,
    bool IncludeOmnibusEditions
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
    int? DurationInSeconds,
    string? CoverFilePath
);

public record SeriesDetailDto(
    SeriesOverviewDto Overview,
    SeriesOwnedBookPageDto OwnedBooks,
    SeriesExpectedBookPageDto MissingBooks,
    SeriesExpectedBookPageDto IgnoredBooks
);

/// <summary>
/// One page of series overviews plus the total number of series matching the filters, so the
/// client can size its pager without asking again.
///
/// Paged because the endpoint this replaces returned every distinct series value in the library
/// at once - with a catalog holding one spelling per book that is every book row projected in
/// memory - and the page rendered every row into the DOM.
/// </summary>
public record SeriesOverviewPageDto(
    List<SeriesOverviewDto> Items,
    int TotalCount
);

/// <summary>Total/matched/unmatched series counts for the overview header, independent of any page.</summary>
public record SeriesCountsDto(
    int Total,
    int Matched,
    int Unmatched
);

public record SeriesOwnedBookPageDto(List<SeriesOwnedBookDto> Items, int TotalCount);

public record SeriesExpectedBookPageDto(List<SeriesExpectedBookDto> Items, int TotalCount);

public record SeriesMatchCandidateDto(
    string SourceName,
    string SourceId,
    string SeriesName,
    string? SourceUrl,
    List<string> Authors,
    int? BookCount,
    double Confidence
);

public record SeriesBookCandidateDto(
    long AudiobookId,
    string BookName,
    string? Series,
    string? SeriesPart,
    int Year,
    List<string> Authors,
    double TitleSimilarity,
    bool AuthorMatches
);

public class MatchSeriesDto
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public bool IncludeOmnibusEditions { get; set; }
}

public class IncludeOmnibusEditionsDto
{
    public bool IncludeOmnibusEditions { get; set; }
}

/// <summary>
/// Addresses a roster entry by its natural key. Row ids are not stable across a re-match or
/// refresh, so the ignore endpoints take position and/or title instead.
/// </summary>
public class ExpectedBookRefDto
{
    public string? Position { get; set; }
    public string? Title { get; set; }
}

/// <summary>
/// Applies a missing expected book to a chosen library audiobook: the audiobook receives the
/// series name and the roster entry's position. The roster entry is addressed by its natural key
/// like <see cref="ExpectedBookRefDto"/> - row ids are not stable across a re-match or refresh.
/// </summary>
public class ApplyExpectedBookDto
{
    public long AudiobookId { get; set; }

    public string? Position { get; set; }

    public string? Title { get; set; }
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
