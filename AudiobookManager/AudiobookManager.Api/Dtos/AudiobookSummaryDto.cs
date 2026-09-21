namespace AudiobookManager.Api.Dtos;

public record AudiobookSummaryDto(
    long Id,
    string? BookName,
    string? Subtitle,
    string? Series,
    string? SeriesPart,
    int? Year,
    List<string> Authors,
    List<string> Narrators,
    List<string> Genres,
    string? CoverFilePath,
    int? DurationInSeconds,
    /// <summary>
    /// Whether <see cref="MatchedSourceName"/> resolves to a real metadata source - mirrors
    /// <see cref="SeriesOverviewDto.IsMatched"/>. A book only ever has a single flat
    /// MatchedSourceName column (kept in sync with Www - see Audiobook.MatchedSourceName's doc),
    /// so there is no separate match confidence or source id/url to carry, unlike a series match.
    /// </summary>
    bool IsMatched,
    string? MatchedSourceName
);
