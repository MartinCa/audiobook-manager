namespace AudiobookManager.Database.Repositories;

/// <summary>
/// One non-ignored expected book reduced to what the bulk author-list filter needs - who it is
/// attributed to, a bare summary of the book, and the series context the per-author classifier
/// needs to match it correctly against owned books (the local series name when the book is linked
/// to a catalog series, the source-series id when the source places it in a series no local row is
/// matched to yet, and the series position). See
/// <see cref="IExpectedBookRepository.GetActiveAuthorBookRefsAsync"/>, which returns these capped
/// at the caller's limit plus one with an overflow flag.
/// </summary>
public record ExpectedBookAuthorBookRef(
    long PersonId,
    long ExpectedBookId,
    string Title,
    int? Year,
    DateOnly? ReleaseDate,
    string? SeriesName,
    string? SourceSeriesId,
    string? SeriesPart);