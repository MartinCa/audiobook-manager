using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public interface ILibraryConsistencyService
{
    Task<(int BooksChecked, int IssuesFound)> RunConsistencyCheck(Func<string, int, int, int, Task> progressAction);
    Task<List<ConsistencyIssue>> RecheckAudiobookAsync(long audiobookId);
    Task<List<TagMismatchField>> GetTagMismatchFieldsAsync(long issueId);
    Task<ConsistencyResolveResult> ResolveTagMismatchSelectivelyAsync(long issueId, IReadOnlyDictionary<string, string?> fieldValues);
    Task<ConsistencyResolveResult> ResolveIssue(long issueId);
    Task<(int resolved, int failed)> ResolveIssuesByType(string issueType, Func<int, int, int, int, Task>? progressAction = null);
    Task<(int resolved, int failed)> ResolveIssues(IEnumerable<long> issueIds, Func<int, int, int, int, Task>? progressAction = null);

    /// <summary>
    /// Validates a bulk resolve-by-type request without resolving anything. Callers that hand the
    /// actual work to a fire-and-forget runner use this to refuse first: an exception thrown
    /// inside background work reaches the client only as a zeroed completion event, so a refusal
    /// (unknown type, unavailable library, implausible MissingMediaFile sweep) must surface
    /// synchronously, as the response to the request that started it.
    /// </summary>
    Task ValidateResolveByTypeAsync(string issueType);
    Task<OrphanDirectoryResolveResult> ResolveOrphanDirectory(long orphanDirectoryId);
    Task<(int resolved, int failed, int retained)> ResolveAllOrphanDirectories();
}
