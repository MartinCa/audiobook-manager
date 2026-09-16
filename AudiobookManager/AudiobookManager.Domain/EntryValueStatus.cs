namespace AudiobookManager.Domain;

/// <summary>
/// Which free-text value kind the entry-status classification is being asked about. Authors and
/// narrators are Person rows (identity exists); series are tag values with no identity of their
/// own.
/// </summary>
public enum EntryValueKind
{
    Author,
    Series,
    Narrator,
}

/// <summary>
/// A single existing library value surfaced as an exact or near match for a typed entry.
/// Id is null for series values, which have no identity beyond the tag string.
/// </summary>
public record EntryValueMatch(long? Id, string Name);

/// <summary>
/// The classification of one typed author/series entry against the library's existing values:
/// Exact when the value already exists (accent/case-insensitive), Similar when only near matches
/// exist, New when neither. The matching is bounded: one input value, a capped candidate
/// prefilter, a capped result list.
/// </summary>
public record EntryValueStatus(
    string Value,
    EntryValueStatusKind Kind,
    EntryValueMatch? ExactMatch,
    List<EntryValueMatch> SimilarMatches);

public enum EntryValueStatusKind
{
    Exact,
    Similar,
    New,
}

/// <summary>
/// One other book in the library that already carries the (series, series part) combination a
/// user is typing into an edit form - advisory output of the series-part conflict check.
/// </summary>
public record SeriesPartConflict(long AudiobookId, string BookName, string? SeriesPart);

/// <summary>
/// The advisory series-part conflict check result. <see cref="Truncated"/> tells the caller the
/// check found more <see cref="Conflicts"/> than the bounded response carries, so the UI can say
/// the list is partial instead of pretending it is complete.
/// </summary>
public record SeriesPartConflictCheck(List<SeriesPartConflict> Conflicts, bool Truncated);