namespace AudiobookManager.Database.Repositories;

/// <summary>
/// An author reduced to what list/search views actually render. Loading the full
/// <see cref="Models.Person"/> graph with its BooksAuthored collection just to read
/// <c>BooksAuthored.Count</c> materializes every audiobook row - descriptions included - once
/// per author, so the count is projected in SQL instead.
/// </summary>
public record AuthorSummaryRow(long Id, string Name, int BookCount);

/// <summary>A book reduced to the id/title pair the similar-value grouping needs.</summary>
public record AuthorBookRef(long Id, string BookName);
