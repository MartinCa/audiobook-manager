namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The minimal per-book input the fuzzy roster reconciliation needs from a series' owned books:
/// the series part and book name, plus the row id so an owned book matched to a roster entry can
/// be reported (and later fixed) when its stored part disagrees with the roster's position. No
/// authors, years or full entities - the detail page's missing/ignored/part-mismatch computation
/// compares these against roster entries, and it is the one read that touches every owned book of
/// a series, so everything else stays out of it.
/// </summary>
public record SeriesOwnedKey(long AudiobookId, string? SeriesPart, string BookName);