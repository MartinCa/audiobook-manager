namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One row of the unified upcoming-releases view - either a legacy scraped release or a
/// followed/matched series'/author's roster entry (see
/// <see cref="AudiobookManager.Services.UpcomingReleaseSource"/>). <see cref="Source"/> tells the
/// client which removal call applies: <c>"Legacy"</c> uses <c>Id</c> with
/// <c>DELETE api/UpcomingReleases/{id}</c>; <c>"Roster"</c> has no <c>Id</c> and is dismissed via
/// <c>POST api/UpcomingReleases/dismiss-roster</c> (or the series/author expected-book ignore
/// endpoints directly), addressed by <c>SeriesName</c>+<c>SeriesPosition</c>+<c>Title</c> or
/// <c>AuthorId</c>+<c>Title</c>.
/// </summary>
public record UpcomingReleaseDto(
    string Source,
    long? Id,
    string Title,
    DateOnly? ReleaseDate,
    int? Year,
    long? AuthorId,
    string? AuthorName,
    long? SeriesId,
    string? SeriesName,
    string? SeriesPosition,
    string SourceName,
    string? SourceUrl,
    string? ImageUrl
);

/// <summary>
/// Addresses a roster-derived upcoming-release item to dismiss - exactly one of
/// (<see cref="SeriesName"/> [+ <see cref="SeriesPosition"/>]) or <see cref="AuthorId"/> must be
/// set, matching how the item's <see cref="UpcomingReleaseDto.Source"/> was <c>"Roster"</c>.
/// </summary>
public class DismissRosterUpcomingReleaseDto
{
    public string? SeriesName { get; set; }
    public string? SeriesPosition { get; set; }
    public long? AuthorId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public record AuthorFollowStatusDto(bool IsFollowed);

public record SeriesFollowStatusDto(bool IsFollowed);

public record AuthorMatchCandidateDto(
    string SourceId,
    string SourceName,
    string Name,
    string? SourceUrl,
    int? BookCount
);

public record AuthorMatchStatusDto(
    string? SourceId,
    string? SourceName,
    string? SourceUrl
);

public record MatchAuthorDto(string SourceId, string SourceName, string? SourceUrl);
