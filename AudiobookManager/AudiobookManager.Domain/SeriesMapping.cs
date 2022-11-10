namespace AudiobookManager.Domain;
public class SeriesMapping
{
    public long? Id { get; set; }
    public string Regex { get; set; }
    public string MappedSeries { get; set; }
    public bool WarnAboutParth { get; set; }

    public SeriesMapping(long? id, string regex, string mappedSeries, bool warnAboutParth)
    {
        Id = id;
        Regex = regex;
        MappedSeries = mappedSeries;
        WarnAboutParth = warnAboutParth;
    }
}
