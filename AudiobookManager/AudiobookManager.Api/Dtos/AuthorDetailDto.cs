namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One author's detail: the summary plus one page of each of the three sections. The sections were
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
    List<AuthorExpectedBookDto>? IgnoredBooks = null,
    /// <summary>
    /// The paged missing-series section. Computed only when the caller asks for it
    /// (<c>includeMissingSeries: true</c> - the reconciliation pass that feeds it is per-author
    /// work the default author detail call has no need for), so it is null for a request that
    /// did not opt in. Bounded per author by the reconciliation provider's series-group cap and
    /// paged here, so it never leaves the server as a whole list.
    /// </summary>
    PaginatedResult<AuthorMissingSeriesDto>? MissingSeries = null
);

/// <summary>One entry of an author's roster, wire shape - mirrors <see cref="SeriesExpectedBookDto"/> plus the series context the unified rows carry (a book discovered through the author's bibliography can belong to a source series, matched locally or not).</summary>
public record AuthorExpectedBookDto(
    long Id,
    string Title,
    int? Year,
    string? SourceUrl,
    bool IsIgnored,
    DateOnly? ReleaseDate = null,
    /// <summary>Position within the book's series, when the source placed it in one.</summary>
    string? Position = null,
    /// <summary>The catalog series this book is linked to, when a matched local series is known.</summary>
    long? SeriesId = null,
    /// <summary>The name of the matched local series (a rename of the catalog row, so it can differ from the source's spelling).</summary>
    string? SeriesName = null,
    /// <summary>The series name as the source reports it, when it places this book in a series.</summary>
    string? SourceSeriesName = null,
    /// <summary>The metadata source that reported this roster entry (e.g. "Hardcover").</summary>
    string? SourceName = null,
    /// <summary>The source's own book identifier - the dedup identity the author and series rosters share.</summary>
    string? SourceBookId = null,
    /// <summary>A cover image URL from the source, when one is stored for the unified row (see <see cref="AudiobookManager.Database.Models.ExpectedBook.ImageUrl"/>).</summary>
    string? ImageUrl = null
);

public record AuthorRefreshResultDto(bool Success, DateTime? LastRefreshedAt);

/// <summary>
/// One source-series group of a matched author's roster whose local library owns no book in that
/// series - "the author is working on a series you don't own any of". <see cref="SourceName"/> and
/// <see cref="SourceSeriesId"/> are the group's source identity (always present - the source always
/// reports which series a bibliography entry belongs to); <see cref="MatchedSeriesId"/>/
/// <see cref="MatchedSeriesName"/> are the matched local catalog series once this source series has
/// been matched to one, null while it is unmatched.
/// </summary>
public record AuthorMissingSeriesDto(
    string SourceName,
    string SourceSeriesId,
    string? SourceSeriesName,
    int ExpectedCount,
    int MissingCount,
    int UpcomingCount,
    int OwnedBookCount,
    long? MatchedSeriesId = null,
    string? MatchedSeriesName = null
);

/// <summary>
/// Addresses a roster entry by its stable expected-book id (preferred - the id a row keeps across
/// refreshes, unlike the series position/title natural keys) or, for the compatibility surface, by
/// its title. Carrying the id on this DTO is what lets a person with two same-titled roster entries
/// ignore the exact row, which the title route cannot tell apart. Mirrors series'
/// <c>ExpectedBookRefDto</c> minus the series-only Position field.
/// </summary>
public class AuthorExpectedBookRefDto
{
    /// <summary>The stable unified expected-book row id - preferred over <see cref="Title"/>.</summary>
    public long? Id { get; set; }

    /// <summary>Natural-key fallback for callers that only carry the title.</summary>
    public string? Title { get; set; }
}