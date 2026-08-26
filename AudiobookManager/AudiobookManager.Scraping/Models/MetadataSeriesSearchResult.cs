namespace AudiobookManager.Scraping.Models;
public class MetadataSeriesSearchResult
{
    public string SeriesName { get; set; }
    public string? SeriesPart { get; set; }
    public string? OriginalSeriesName { get; set; }
    public bool? PartWarning { get; set; }

    public MetadataSeriesSearchResult(string seriesName)
    {
        SeriesName = seriesName;
    }
}
