namespace AudiobookManager.Services;

public record MissingTagField(string Key, string Label, bool IsCriticalByDefault);

public record AudiobookMissingTags(long AudiobookId, string BookName, List<string> Authors, List<string> MissingFields);

public interface IMissingTagService
{
    List<MissingTagField> GetTaggableFields();

    /// <summary>
    /// One page of the audiobooks missing any selected field, with the full matching total so the
    /// client can size its pager. Everything happens in SQL: the selected fields' "is missing"
    /// predicates (defined by the hand-maintained field list that is the single source of truth)
    /// filter the rows, <paramref name="search"/> folds accents on the book name, and the page is
    /// counted, ordered (book name + id) and sliced by the repository - only the returned page's
    /// rows are materialized into per-book projections. Bounded because the unpaged version
    /// returned a book missing one of millions of rows' critical tags for the session.
    /// </summary>
    Task<(List<AudiobookMissingTags> Items, int Total)> FindAudiobooksMissingTagsPageAsync(
        IEnumerable<string> fieldKeys, string? search, int skip, int take);
}
