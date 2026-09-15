using AudiobookManager.Domain;

namespace AudiobookManager.Api.Dtos;

/// <summary>
/// Maps the domain overview the series catalog computes to its wire DTO. Shared by the series
/// overview page and the author detail's series section so both render the same rich entry.
/// </summary>
public static class SeriesOverviewMapper
{
    public static SeriesOverviewDto ToDto(SeriesOverview o) => new(
        o.Id,
        o.Name,
        o.Authors,
        o.OwnedBookCount,
        o.IsMatched,
        o.MatchedSourceName,
        o.MatchedSourceId,
        o.MatchedSourceUrl,
        o.MatchConfidence,
        o.LastRefreshedAt,
        o.ExpectedBookCount,
        o.MissingBookCount,
        o.IgnoredBookCount,
        o.IncludeOmnibusEditions);
}
