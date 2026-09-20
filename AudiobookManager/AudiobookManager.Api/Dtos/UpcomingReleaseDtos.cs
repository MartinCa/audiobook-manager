namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One row of the unified upcoming-releases view - either a legacy scraped release or a
/// followed/matched series'/author's roster entry (see
/// <see cref="AudiobookManager.Services.UpcomingReleaseSource"/>). <see cref="Source"/> tells the
/// client which removal call applies: <c>"Legacy"</c> uses <c>Id</c> with
/// <c>DELETE api/UpcomingReleases/{id}</c>; <c>"Roster"</c> has no <c>Id</c> and is dismissed via
/// <c>POST api/UpcomingReleases/dismiss-roster</c> (or the series/author expected-book ignore
/// endpoints directly), addressed preferably by <see cref="ExpectedBookId"/> - the stable unified
/// row id a roster item always carries - or by the source identity
/// (<see cref="SourceName"/> + <see cref="SourceBookId"/>), with the
/// <c>SeriesName</c>+<c>SeriesPosition</c>+<c>Title</c> / <c>AuthorId</c>+<c>Title</c> natural-key
/// routes as the compatibility fallback.
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
    string? ImageUrl,
    /// <summary>The stable unified expected-book row id - set only for a roster-derived (<c>"Roster"</c>) item; the row that <c>dismiss-roster</c> sets <c>IsIgnored</c> on.</summary>
    long? ExpectedBookId = null,
    /// <summary>The source's own book identifier - the dedup identity shared with the roster reconciliation; null when the source gave none.</summary>
    string? SourceBookId = null
);

/// <summary>
/// Addresses a roster-derived upcoming-release item to dismiss - the dismiss endpoint prefers
/// <see cref="ExpectedBookId"/> (the stable unified row id a <c>"Roster"</c> item carries), then
/// the source identity (<see cref="SourceName"/> + <see cref="SourceBookId"/>), then, for legacy
/// rows, exactly one of (<see cref="SeriesName"/> [+ <see cref="SeriesPosition"/>]) or
/// <see cref="AuthorId"/> plus <see cref="Title"/>.
/// </summary>
public class DismissRosterUpcomingReleaseDto
{
    public long? ExpectedBookId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceBookId { get; set; }
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
