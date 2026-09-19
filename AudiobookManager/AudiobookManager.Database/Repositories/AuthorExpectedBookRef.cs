namespace AudiobookManager.Database.Repositories;

/// <summary>
/// One non-ignored standalone-book roster entry reduced to what the bulk author
/// missing/upcoming-book reconciliation needs - see
/// <see cref="IPersonRepository.GetAllActiveAuthorExpectedBooksAsync"/>.
/// </summary>
public record AuthorExpectedBookRef(long PersonId, string Title, int? Year, DateOnly? ReleaseDate);
