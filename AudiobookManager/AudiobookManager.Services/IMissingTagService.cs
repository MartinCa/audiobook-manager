using AudiobookManager.Database.Repositories;

namespace AudiobookManager.Services;

public record MissingTagField(string Key, string Label, bool IsCriticalByDefault);

/// <summary>
/// One book on the Missing Tags page: identity, missing-field list, and the same display fields
/// <c>AudiobookSummaryDto</c> carries (mirroring <c>MissingTagRow</c>'s shape) so the page's book
/// list can render exactly like every other book list in the app instead of a bare title/author
/// line.
/// </summary>
public record AudiobookMissingTags(
    long AudiobookId,
    string BookName,
    List<string> Authors,
    List<string> Narrators,
    int Year,
    string? Series,
    string? SeriesPart,
    string? CoverFilePath,
    int? DurationInSeconds,
    bool IsMatched,
    string? MatchedSourceName,
    List<string> MissingFields);

public interface IMissingTagService
{
    List<MissingTagField> GetTaggableFields();

    /// <summary>
    /// One page of the audiobooks missing any selected field, with the full matching total so the
    /// client can size its pager. Everything happens in SQL: the selected fields' "is missing"
    /// predicates (defined by the hand-maintained field list that is the single source of truth)
    /// filter the rows, <paramref name="search"/> folds accents on the book name,
    /// <paramref name="filter"/> is the same <see cref="BookSummaryFilter"/> the whole-library book
    /// list applies, and the page is counted, ordered (book name + id) and sliced by the repository
    /// - only the returned page's rows are materialized into per-book projections. Bounded because
    /// the unpaged version returned a book missing one of millions of rows' critical tags for the
    /// session.
    /// </summary>
    Task<(List<AudiobookMissingTags> Items, int Total)> FindAudiobooksMissingTagsPageAsync(
        IEnumerable<string> fieldKeys, string? search, int skip, int take, BookSummaryFilter? filter = null);
}
