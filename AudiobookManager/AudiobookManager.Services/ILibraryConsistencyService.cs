using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public interface ILibraryConsistencyService
{
    Task<(int BooksChecked, int IssuesFound)> RunConsistencyCheck(Func<string, int, int, int, Task> progressAction);
    Task<List<ConsistencyIssue>> RecheckAudiobookAsync(long audiobookId);
    Task<List<TagMismatchField>> GetTagMismatchFieldsAsync(long issueId);
    Task<ConsistencyResolveResult> ResolveTagMismatchSelectivelyAsync(long issueId, IReadOnlyDictionary<string, string?> fieldValues);
    Task<ConsistencyResolveResult> ResolveIssue(long issueId);
    Task<(int resolved, int failed)> ResolveIssuesByType(string issueType);
    Task<(int resolved, int failed)> ResolveIssues(IEnumerable<long> issueIds);
    Task<OrphanDirectoryResolveResult> ResolveOrphanDirectory(long orphanDirectoryId);
    Task<(int resolved, int failed, int retained)> ResolveAllOrphanDirectories();
}
