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

    /// <summary>
    /// Whether omnibus/box-set editions from the matched source are kept on the roster instead
    /// of being filtered out.
    /// </summary>
    public bool IncludeOmnibusEditions { get; set; }
}

/// <summary>
/// One paged slice of the series detail: the overview plus one page of each section and
/// that section's full total, so the client can size every section's pager from a single response.
/// The lists are already sorted with the tiebreakers that make paging stable. (The unpaged
/// <c>SeriesDetail</c> model this replaced was removed with the response it served: there is no
/// section request that should materialize a series' whole roster any more.)
/// </summary>
public class SeriesDetailPage
{
    public SeriesOverview Overview { get; set; } = new();
    public List<SeriesOwnedBook> OwnedBooks { get; set; } = new();
    public int OwnedBookTotal { get; set; }
    public List<SeriesExpectedBookInfo> MissingBooks { get; set; } = new();
    public int MissingBookTotal { get; set; }
    public List<SeriesExpectedBookInfo> IgnoredBooks { get; set; } = new();
    public int IgnoredBookTotal { get; set; }
}

/// <summary>One page of series overviews plus the total number of series matching the filters.</summary>
public class SeriesOverviewPage
{
    public List<SeriesOverview> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>
/// The overview header numbers: how many distinct series values there are, and how many of those
/// are matched to a metadata source. Unmatched is derived, not stored, so it always agrees with
/// the other two.
/// </summary>
public class SeriesOverviewCounts
{
    public int Total { get; set; }
    public int Matched { get; set; }
    public int Unmatched { get; set; }
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

/// <summary>
/// A library audiobook suggested as a match for one missing expected book of a matched series.
/// </summary>
public class SeriesBookCandidate
{
    public long AudiobookId { get; set; }
    public string BookName { get; set; } = string.Empty;

    /// <summary>The candidate's current series value, if any - applying moves it to the target series.</summary>
    public string? Series { get; set; }
    public string? SeriesPart { get; set; }
    public int Year { get; set; }
    public List<string> Authors { get; set; } = new();

    /// <summary>0..1 normalized similarity of the candidate's book name to the expected book's title.</summary>
    public double TitleSimilarity { get; set; }

    /// <summary>Whether a candidate author closely matches a known author of the series (tier-1 evidence).</summary>
    public bool AuthorMatches { get; set; }
}
