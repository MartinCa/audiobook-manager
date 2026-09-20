using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The computed result of reconciling a matched author's roster against the author's owned books -
/// the author-detail page's counterpart of <see cref="SeriesReconciliation"/>. The roster now lives
/// on the unified <see cref="AudiobookManager.Database.Models.ExpectedBook"/> table and covers the
/// author's whole bibliography, series books included (a series refresh and the author refresh
/// report the same book as one row, so the series-linked entries here are the unified rows' series
/// placements, not double-counted rows). There is no part-mismatch section: the author view does
/// not fix a book's part - that is the series view's job once the series is matched.
/// </summary>
public sealed record AuthorReconciliation(
    IReadOnlyList<AuthorExpectedBookInfo> Missing,
    IReadOnlyList<AuthorExpectedBookInfo> Upcoming,
    IReadOnlyList<AuthorExpectedBookInfo> Ignored,
    int ExpectedBookCount,
    int OwnedCount,
    // The author's source-series groups with zero owned books locally - see
    // AuthorMissingSeriesInfo. Bounded: only a per-author detail computation, capped at
    // AuthorReconciliationProvider.MaxAuthorSeriesGroups.
    IReadOnlyList<AuthorMissingSeriesInfo> MissingSeries)
{
    public int MissingBookCount => Missing.Count;
    public int UpcomingBookCount => Upcoming.Count;
    public int IgnoredBookCount => Ignored.Count;
    public int MissingSeriesCount => MissingSeries.Count;
}

/// <summary>
/// One entry of an author's roster, read-side shape - mirrors <see cref="SeriesExpectedBookInfo"/>
/// plus the series context the unified rows carry (a book discovered through the author's
/// bibliography can belong to a source series, matched locally or not).
/// </summary>
public sealed class AuthorExpectedBookInfo
{
    /// <summary>The stable unified <see cref="AudiobookManager.Database.Models.ExpectedBook"/> row id.</summary>
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? SourceUrl { get; set; }
    public bool IsIgnored { get; set; }

    /// <summary>The metadata source that reported this roster entry (e.g. "Hardcover").</summary>
    public string? SourceName { get; set; }

    /// <summary>The source's own book identifier, when it reported one - the dedup identity the author and series rosters share.</summary>
    public string? SourceBookId { get; set; }

    /// <summary>A cover image URL from the source, when one is stored for the unified row (see <see cref="AudiobookManager.Database.Models.ExpectedBook.ImageUrl"/>).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Position within the book's series, when the source placed it in one.</summary>
    public string? Position { get; set; }

    /// <summary>The catalog series this book is linked to, when a matched local series is known.</summary>
    public long? SeriesId { get; set; }

    /// <summary>The name of the matched local series (a rename of the catalog row, so it can differ from the source's spelling).</summary>
    public string? SeriesName { get; set; }

    /// <summary>The source's series id, when the source places this book in a series (even an unmatched one).</summary>
    public string? SourceSeriesId { get; set; }

    /// <summary>The series name as the source reports it, when it places this book in a series.</summary>
    public string? SourceSeriesName { get; set; }
}

/// <summary>
/// One source-series group of a matched author's roster whose local library owns no book in that
/// series - "the author is working on a series you don't own any of", the author-detail
/// counterpart of the series detail's missing section. A group is *missing* when the matched local
/// series (when one is known) has zero owned books - or when the source series is not yet matched
/// to a local series at all. A group with some owned books is deliberately NOT reported here, but
/// its individual unmatched books still appear in the author's Missing/Upcoming lists (they may be
/// new entries of a partly-owned series, and become reachable through the series view once the
/// series is matched). Bounded per author by
/// <see cref="AuthorReconciliationProvider.MaxAuthorSeriesGroups"/> with an explicit overflow
/// refusal, never a global list.
/// </summary>
public sealed class AuthorMissingSeriesInfo
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceSeriesId { get; set; } = string.Empty;

    /// <summary>The series name as the source reports it, if any.</summary>
    public string? SourceSeriesName { get; set; }

    /// <summary>The matched local catalog series, once this source series has been matched to one.</summary>
    public long? SeriesId { get; set; }
    public string? SeriesName { get; set; }

    /// <summary>How many source books the author's roster lists under this series.</summary>
    public int ExpectedCount { get; set; }

    /// <summary>Of those, how many are released-but-not-owned (unmatched and not upcoming).</summary>
    public int MissingCount { get; set; }

    /// <summary>Of those, how many are upcoming (not yet released and not owned).</summary>
    public int UpcomingCount { get; set; }

    /// <summary>How many owned books of this author sit in the matched local series (always 0 while the series is unmatched).</summary>
    public int OwnedCount { get; set; }
}