namespace AudiobookManager.Scraping.Models;

public class MetadataSearchServiceInfo
{
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }

    public MetadataSearchServiceInfo(string name, bool enabled, string? disabledReason = null)
    {
        Name = name;
        Enabled = enabled;
        DisabledReason = disabledReason;
    }
}
