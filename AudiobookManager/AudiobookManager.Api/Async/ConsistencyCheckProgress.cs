namespace AudiobookManager.Api.Async;

public class ConsistencyCheckProgress
{
    public string Message { get; set; }
    public int BooksChecked { get; set; }
    public int TotalBooks { get; set; }
    public int IssuesFound { get; set; }

    public ConsistencyCheckProgress(string message, int booksChecked, int totalBooks, int issuesFound)
    {
        Message = message;
        BooksChecked = booksChecked;
        TotalBooks = totalBooks;
        IssuesFound = issuesFound;
    }
}
