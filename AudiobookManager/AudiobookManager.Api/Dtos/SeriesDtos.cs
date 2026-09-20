using System.ComponentModel.DataAnnotations;

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
    bool IncludeOmnibusEditions,
    int UpcomingBookCount = 0,
    bool IsFollowed = false
);

public record SeriesExpectedBookDto(
    long Id,
    string Title,
    string? Position,
    int? Year,
    string? SourceUrl,
    bool IsIgnored,
    DateOnly? ReleaseDate = null,
    /// <summary>The metadata source that reported this roster entry (e.g. "Hardcover"), when known.</summary>
    string? SourceName = null,
    /// <summary>The source's own book identifier - the dedup identity the author and series rosters share, when the source gave one.</summary>
    string? SourceBookId = null
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
    SeriesExpectedBookPageDto IgnoredMissingBooks,
    SeriesExpectedBookPageDto IgnoredUpcomingBooks,
    SeriesPartMismatchPageDto PartMismatches,
    SeriesExpectedBookPageDto? UpcomingBooks = null
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

/// <summary>
/// An owned book of a matched series whose stored part is missing or differs from the position
/// its matching roster entry assigns. The expected part and roster title are what the client
/// hands back to <c>/series/expected-books/apply</c> to fix the book, so <paramref name="StoredPart"/>
/// is nullable (a book with no part at all is exactly the missing-part shape this section exists
/// to surface) while the expected values are not.
/// </summary>
public record SeriesPartMismatchDto(
    long AudiobookId,
    string BookName,
    string? StoredPart,
    string ExpectedPart,
    string RosterTitle
);

public record SeriesPartMismatchPageDto(List<SeriesPartMismatchDto> Items, int TotalCount);

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

/// <summary>
/// One row of the bulk missing-book match view: a missing expected book (the same shape the
/// detail page's missing section renders) plus its ranked candidate library audiobooks. The
/// candidate list is bounded per row by the same cap the single-book candidates endpoint uses.
/// </summary>
public record SeriesBulkCandidateItemDto(
    SeriesExpectedBookDto Book,
    List<SeriesBookCandidateDto> Candidates
);

public record SeriesBulkCandidatePageDto(
    List<SeriesBulkCandidateItemDto> Items,
    int TotalCount
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
/// Addresses a roster entry by its natural key - the series-scoped API's pre-unification
/// compatibility surface, taking only what the source reported (position and/or title, the pair
/// the client's missing/mismatch flows carry). The unified expected-book row keeps a stable id
/// across refreshes (rows are refreshed in place, never re-created), which is why the
/// id-addressed dismissal routes (the author detail's <c>AuthorExpectedBookRefDto</c>) are
/// preferred for callers that know an id; this route stays on the natural key as the original
/// series contract.
/// </summary>
public class ExpectedBookRefDto
{
    public string? Position { get; set; }
    public string? Title { get; set; }
}

/// <summary>
/// Applies a missing expected book to a chosen library audiobook: the audiobook receives the
/// series name and the roster entry's position. The roster entry is addressed by its natural key
/// like <see cref="ExpectedBookRefDto"/> - the pair the source reports, not a row id.
/// </summary>
public class ApplyExpectedBookDto
{
    public long AudiobookId { get; set; }

    public string? Position { get; set; }

    public string? Title { get; set; }
}

/// <summary>
/// One accepted assignment in a bulk missing-book apply. The roster entry is addressed by its
/// natural key (position and/or title) exactly like <see cref="ApplyExpectedBookDto"/>;
/// a "do not assign" row is a selection the client simply omits, never a null AudiobookId.
/// </summary>
public class ApplyMissingBookSelectionDto
{
    public string? Position { get; set; }

    public string? Title { get; set; }

    [Required]
    public long AudiobookId { get; set; }
}

public class ApplyMissingBookBulkRequestDto
{
    [Required]
    public List<ApplyMissingBookSelectionDto> Selections { get; set; } = new();
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
