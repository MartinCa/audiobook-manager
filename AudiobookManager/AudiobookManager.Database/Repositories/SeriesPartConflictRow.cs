namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The slice of an audiobook the advisory series-part conflict check reads: identity, book name
/// and series part. Projected in SQL so the check never materializes full entity graphs for every
/// book in a series - its whole point is to be cheap enough to run on every edit keystroke.
/// </summary>
public record SeriesPartConflictRow(long AudiobookId, string BookName, string? SeriesPart);