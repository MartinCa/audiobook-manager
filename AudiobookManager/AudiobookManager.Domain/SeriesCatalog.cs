namespace AudiobookManager.Domain;

/// <summary>
/// Read-side view of one series value found in the library, enriched with whatever the
/// series catalog knows about its full roster.
/// </summary>
public class SeriesOverview
{
    /// <summary>
    /// Catalog row id, or null when this series value has never been matched (it exists
    /// only as free text on audiobooks).
    /// </summary>
    public long? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = new();
    public int OwnedBookCount { get; set; }
    public bool IsMatched { get; set; }
    public string? MatchedSourceName { get; set; }
    public string? MatchedSourceId { get; set; }
    public string? MatchedSourceUrl { get; set; }
    public double? MatchConfidence { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public int ExpectedBookCount { get; set; }
    public int MissingBookCount { get; set; }
    public int IgnoredBookCount { get; set; }
}

public class SeriesDetail
{
    public SeriesOverview Overview { get; set; } = new();
    public List<SeriesOwnedBook> OwnedBooks { get; set; } = new();
    public List<SeriesExpectedBookInfo> MissingBooks { get; set; } = new();
    public List<SeriesExpectedBookInfo> IgnoredBooks { get; set; } = new();
}

public class SeriesOwnedBook
{
    public long Id { get; set; }
    public string BookName { get; set; } = string.Empty;
    public string? SeriesPart { get; set; }
    public int Year { get; set; }
    public List<string> Authors { get; set; } = new();
    public List<string> Narrators { get; set; } = new();
    public int? DurationInSeconds { get; set; }
}

public class SeriesExpectedBookInfo
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Position { get; set; }
    public int? Year { get; set; }
    public string? SourceUrl { get; set; }
    public bool IsIgnored { get; set; }
}

public class SeriesMatchCandidate
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SeriesName { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public List<string> Authors { get; set; } = new();
    public int? BookCount { get; set; }

    /// <summary>0..1 - how confidently this candidate is the same series as the library value.</summary>
    public double Confidence { get; set; }
}
