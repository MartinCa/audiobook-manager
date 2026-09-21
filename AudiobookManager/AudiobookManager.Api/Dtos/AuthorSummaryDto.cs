namespace AudiobookManager.Api.Dtos;

public record AuthorSummaryDto(
    long Id,
    string Name,
    int BookCount,
    /// <summary>
    /// Whether this author is matched to a metadata source - mirrors
    /// <see cref="SeriesOverviewDto.IsMatched"/>/<see cref="MatchedSourceName"/>. Sourced from
    /// <c>Person.MatchedSourceId</c>/<c>MatchedSourceName</c> (see that model's doc); there is no
    /// separate match confidence for an author match, unlike a series match.
    /// </summary>
    bool IsMatched,
    string? MatchedSourceName
);
