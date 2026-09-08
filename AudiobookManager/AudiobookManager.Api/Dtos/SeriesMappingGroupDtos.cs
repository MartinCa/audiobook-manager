using AudiobookManager.Domain;

namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One series-regex-mapping group: all mapping rows targeting one canonical series name, for the
/// Settings page. Grouped server-side so the client renders the shape instead of reducing a flat
/// table into buckets itself.
/// </summary>
public record SeriesMappingGroupDto(
    string MappedSeries,
    List<SeriesMapping> Items
);

/// <summary>All groups matching the search, plus the total number of mapping rows (the page badge).</summary>
public record SeriesMappingGroupsDto(
    List<SeriesMappingGroupDto> Items,
    int Total
);