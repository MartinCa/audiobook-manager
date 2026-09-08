namespace AudiobookManager.Domain;
public class SeriesMapping
{
    public long? Id { get; set; }
    public string Regex { get; set; }
    public string MappedSeries { get; set; }
    public bool WarnAboutPart { get; set; }

    public SeriesMapping(long? id, string regex, string mappedSeries, bool warnAboutParth)
    {
        Id = id;
        Regex = regex;
        MappedSeries = mappedSeries;
        WarnAboutPart = warnAboutParth;
    }
}

/// <summary>
/// All mapping rows that target one canonical series name, plus their target. The grouped shape is
/// what the Settings page renders; grouping lives on the server so the client no longer reduces a
/// flat table into group buckets itself.
/// </summary>
public class SeriesMappingGroup
{
    public string MappedSeries { get; set; } = string.Empty;
    public List<SeriesMapping> Mappings { get; set; } = new();
}

/// <summary>All groups, with the total number of mapping rows (the badge the page header shows).</summary>
public class SeriesMappingGroups
{
    public List<SeriesMappingGroup> Items { get; set; } = new();
    public int Total { get; set; }
}
