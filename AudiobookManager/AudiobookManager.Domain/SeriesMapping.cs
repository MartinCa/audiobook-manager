namespace AudiobookManager.Domain;

/// <summary>
/// One regex mapping pattern owned by a series. The pattern has no target of its own: when the
/// incoming metadata series value matches <see cref="Regex"/>, <see cref="AudiobookManager.Scraping.BookSeriesMapper"/>
/// rewrites it to the owning series' name. <see cref="Id"/> is null only while the mapping has not
/// been persisted (the same model doubles as the create-request body).
/// </summary>
public class SeriesMapping
{
    public long? Id { get; set; }
    public string Regex { get; set; }
    public bool WarnAboutPart { get; set; }

    public SeriesMapping(long? id, string regex, bool warnAboutPart)
    {
        Id = id;
        Regex = regex;
        WarnAboutPart = warnAboutPart;
    }
}
