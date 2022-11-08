namespace AudiobookManager.Scraping.Models;
public class BookSeriesSearchResult
{
    public string SeriesName { get; set; }
    public string? SeriesPart { get; set; }
    public string? OriginalSeriesName { get; set; }
    public bool? PartWarning { get; set; }

    public BookSeriesSearchResult(string seriesName)
    {
        SeriesName = seriesName;
    }
}
