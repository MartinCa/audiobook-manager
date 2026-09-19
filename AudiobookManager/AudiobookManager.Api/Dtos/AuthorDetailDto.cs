namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One author's detail: the summary plus one page of each of the two sections. The sections were
/// whole lists of an author's entire catalogue; they are paged now so a heavy author cannot send
/// every series and standalone book over the wire and into the DOM at once.
/// </summary>
public record AuthorDetailDto(
    AuthorSummaryDto Author,
    PaginatedResult<SeriesOverviewDto> Series,
    PaginatedResult<AudiobookSummaryDto> StandaloneBooks,
    DateTime? LastRefreshedAt = null,
    List<AuthorExpectedBookDto>? MissingBooks = null,
    List<AuthorExpectedBookDto>? UpcomingBooks = null,
    List<AuthorExpectedBookDto>? IgnoredBooks = null
);

/// <summary>One entry of an author's standalone-books roster, wire shape - mirrors <see cref="SeriesExpectedBookDto"/> minus the series-only Position field.</summary>
public record AuthorExpectedBookDto(
    long Id,
    string Title,
    int? Year,
    string? SourceUrl,
    bool IsIgnored,
    DateOnly? ReleaseDate = null
);

public record AuthorRefreshResultDto(bool Success, DateTime? LastRefreshedAt);

/// <summary>
/// Addresses a standalone-books roster entry by its natural key (title) - row ids are not stable
/// across a re-refresh (<see cref="AudiobookManager.Services.IUpcomingReleaseService.RefreshAuthorRosterAsync"/>
/// deletes and re-inserts the whole roster). Mirrors series' <c>ExpectedBookRefDto</c> minus the
/// series-only Position field.
/// </summary>
public class AuthorExpectedBookRefDto
{
    public string? Title { get; set; }
}
