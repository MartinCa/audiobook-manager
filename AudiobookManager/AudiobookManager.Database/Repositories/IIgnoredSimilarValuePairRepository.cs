using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IIgnoredSimilarValuePairRepository
{
    /// <summary>Every ignored pair for one kind ("authors"/"series"). Naturally small - one row per user-marked pair.</summary>
    Task<List<IgnoredSimilarValuePair>> GetForKindAsync(string kind);

    /// <summary>
    /// Idempotently adds the given (ValueA, ValueB) pairs for a kind - pairs that already exist
    /// are skipped rather than duplicated. <paramref name="pairs"/> is expected already ordered
    /// (ValueA &lt;= ValueB by <see cref="StringComparer.Ordinal"/>), e.g. via
    /// <see cref="Services.Similarity.SimilarityGrouper.IgnoredPairKey"/>.
    /// </summary>
    Task AddRangeAsync(string kind, IEnumerable<(string ValueA, string ValueB)> pairs);

    /// <summary>
    /// Idempotent: removing an id that does not exist, already removed, or belongs to a
    /// different <paramref name="kind"/> is a no-op success.
    /// </summary>
    Task DeleteAsync(string kind, long id);
}
