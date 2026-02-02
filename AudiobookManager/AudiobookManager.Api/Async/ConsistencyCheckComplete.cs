namespace AudiobookManager.Api.Async;

public class ConsistencyCheckComplete
{
    public int TotalBooksChecked { get; set; }
    public int TotalIssuesFound { get; set; }

    public ConsistencyCheckComplete(int totalBooksChecked, int totalIssuesFound)
    {
        TotalBooksChecked = totalBooksChecked;
        TotalIssuesFound = totalIssuesFound;
    }
}
