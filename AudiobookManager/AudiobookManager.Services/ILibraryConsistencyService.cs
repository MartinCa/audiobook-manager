namespace AudiobookManager.Services;

public interface ILibraryConsistencyService
{
    Task RunConsistencyCheck(Func<string, int, int, int, Task> progressAction);
    Task ResolveIssue(long issueId);
    Task<(int resolved, int failed)> ResolveIssuesByType(string issueType);
}
