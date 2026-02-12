namespace AudiobookManager.Scraping.Models;

public class SearchServiceInfo
{
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }

    public SearchServiceInfo(string name, bool enabled, string? disabledReason = null)
    {
        Name = name;
        Enabled = enabled;
        DisabledReason = disabledReason;
    }
}
