namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The slice of an audiobook the series overview actually needs: its series value, the
/// fields used to decide whether a roster entry is owned, and its author names. Projected
/// straight out of the database so the whole library can be grouped without materializing
/// full entities (and their narrators and genres).
/// </summary>
public record SeriesGroupingBook(string Series, string? SeriesPart, string BookName, List<string> Authors);
