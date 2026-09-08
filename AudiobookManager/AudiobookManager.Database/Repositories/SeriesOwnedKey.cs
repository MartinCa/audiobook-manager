namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The minimal per-book input the fuzzy roster reconciliation needs from a series' owned books:
/// the series part and book name only. No authors, years, ids or full entities - the detail
/// page's missing/ignored computation compares these against roster entries, and it is the one
/// read that touches every owned book of a series, so everything else stays out of it.
/// </summary>
public record SeriesOwnedKey(string? SeriesPart, string BookName);