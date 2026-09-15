namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One other book flagged by the advisory series-part conflict check, with the identity the
/// edit form's warning needs to link to the book's detail page.
/// </summary>
public record SeriesPartConflictBookDto(long AudiobookId, string BookName, string? SeriesPart);

/// <summary>
/// The advisory series-part conflict check result. Truncated is true when the check found more
/// conflicts than the bounded response carries - the UI must say the list is partial rather than
/// pretend it is complete.
/// </summary>
public record SeriesPartConflictCheckDto(List<SeriesPartConflictBookDto> Conflicts, bool Truncated);