namespace AudiobookManager.Api.Async;

public class ConsistencyCheckComplete
{
    public int TotalBooksChecked { get; set; }
    public int TotalIssuesFound { get; set; }
    public string Scope { get; set; } = ConsistencyCheckScope.Library;

    public ConsistencyCheckComplete(
        int totalBooksChecked,
        int totalIssuesFound,
        string scope = ConsistencyCheckScope.Library)
    {
        TotalBooksChecked = totalBooksChecked;
        TotalIssuesFound = totalIssuesFound;
        Scope = scope;
    }
}