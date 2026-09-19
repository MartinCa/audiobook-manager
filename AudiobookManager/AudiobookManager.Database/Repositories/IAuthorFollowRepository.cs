using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IAuthorFollowRepository
{
    Task<bool> IsFollowedAsync(long personId);

    /// <summary>Idempotent: following an already-followed author is a no-op success.</summary>
    Task FollowAsync(long personId);

    /// <summary>Idempotent: unfollowing an author with no follow row is a no-op success.</summary>
    Task UnfollowAsync(long personId);

    /// <summary>
    /// Every followed author that is also matched to a Hardcover author - the worker's poll
    /// list. An author followed before being matched (or after its match was cleared) is
    /// skipped until it has one.
    /// </summary>
    Task<List<Person>> GetFollowedMatchedAuthorsAsync();
}
