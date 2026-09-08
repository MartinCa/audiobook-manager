namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The slice of an audiobook the Missing Tags check reads for every book: identity, book name,
/// author names, and one precomputed boolean per taggable field saying whether that field is
/// missing. Projected in SQL (the booleans are EXISTS/subquery expressions, the author list a
/// correlated projection) so the check no longer materializes full entities - Authors, Narrators,
/// Genres collections and Description-size blobs included - for every book in the library.
///
/// The flag semantics deliberately mirror <c>MissingTagService.Fields</c>'s <c>IsMissing</c>
/// predicates; that hand-maintained list stays the single source of truth for what "missing"
/// means, and the rows here exist so it evaluates against one compact per-book projection instead
/// of a materialized entity graph.
/// </summary>
public record MissingTagRow(
    long Id,
    string BookName,
    List<string> Authors,
    bool HasRealAuthor,
    bool HasRealNarrator,
    bool HasGenre,
    bool BookNameBlank,
    bool YearZero,
    bool SeriesBlank,
    bool SeriesPartBlank,
    bool SubtitleBlank,
    bool DescriptionBlank,
    bool LanguageBlank,
    bool CoverBlank,
    bool CopyrightBlank,
    bool PublisherBlank,
    bool RatingBlank,
    bool AsinBlank,
    bool WwwBlank)
{
}