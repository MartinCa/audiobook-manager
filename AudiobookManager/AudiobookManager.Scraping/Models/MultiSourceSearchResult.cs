namespace AudiobookManager.Scraping.Models;

public class MultiSourceSearchResult
{
    public IList<BookSearchResult> Results { get; set; } = new List<BookSearchResult>();
    public IList<SourceSearchStatus> SourceStatuses { get; set; } = new List<SourceSearchStatus>();
}

public class SourceSearchStatus
{
    public string Source { get; set; } = "";
    public bool Success { get; set; }
    public int ResultCount { get; set; }
    public string? Error { get; set; }
}
