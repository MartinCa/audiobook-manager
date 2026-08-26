namespace AudiobookManager.Scraping.Models;

public class MetadataMultiSourceSearchResult
{
    public IList<MetadataSearchResult> Results { get; set; } = new List<MetadataSearchResult>();
    public IList<MetadataSourceSearchStatus> SourceStatuses { get; set; } = new List<MetadataSourceSearchStatus>();
}

public class MetadataSourceSearchStatus
{
    public string Source { get; set; } = "";
    public bool Success { get; set; }
    public int ResultCount { get; set; }
    public string? Error { get; set; }
}
